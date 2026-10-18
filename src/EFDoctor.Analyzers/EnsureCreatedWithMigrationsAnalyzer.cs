using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EnsureCreatedWithMigrationsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD034";
    public const string RuleTitle = "EnsureCreated on a context that uses migrations";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD034";
    public const string Impact = "EnsureCreated creates the schema directly from the current model and records no migration history. On a new database, a later Migrate then tries to create tables that already exist and fails; on an existing database, EnsureCreated does nothing, so new migrations are never applied by this code.";
    public const string Remediation = "Use Database.Migrate() or MigrateAsync(), or apply migrations at deployment with a migration bundle or script, instead of EnsureCreated for a context that has migrations. Keep EnsureCreated for contexts without migrations, such as test or in-memory databases.";

    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string DatabaseFacadeMetadataName = "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade";
    private const string MigrationMetadataName = "Microsoft.EntityFrameworkCore.Migrations.Migration";
    private const string ModelSnapshotMetadataName = "Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot";
    private const string Message = "EnsureCreated bypasses the migrations of '{0}'";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Correctness",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports Database.EnsureCreated or EnsureCreatedAsync on a DbContext type that the same compilation manages with migrations.",
        helpLinkUri: EfHelpLinks.For(DiagnosticId),
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        if (!EfAnalysisScope.Includes(context.Options, context.Compilation))
        {
            return;
        }

        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var databaseFacade = context.Compilation.GetTypeByMetadataName(DatabaseFacadeMetadataName);
        if (dbContext is null || databaseFacade is null)
        {
            return;
        }

        var migration = context.Compilation.GetTypeByMetadataName(MigrationMetadataName);
        var modelSnapshot = context.Compilation.GetTypeByMetadataName(ModelSnapshotMetadataName);
        var migrated = new ConcurrentDictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);
        var candidates = new ConcurrentBag<(IInvocationOperation Call, ITypeSymbol Context)>();

        // Migration and snapshot classes name their context with [DbContext(typeof(T))].
        context.RegisterSymbolAction(
            symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                var kind = migration is not null && EfQueryOperationAnalysis.IsOrDerivesFrom(type.BaseType, migration) ? "migration"
                    : modelSnapshot is not null && EfQueryOperationAnalysis.IsOrDerivesFrom(type.BaseType, modelSnapshot) ? "model snapshot"
                    : null;
                if (kind is null)
                {
                    return;
                }

                foreach (var attribute in type.GetAttributes())
                {
                    if (attribute.AttributeClass?.Name == "DbContextAttribute"
                        && attribute.ConstructorArguments.Length == 1
                        && attribute.ConstructorArguments[0].Value is ITypeSymbol contextType)
                    {
                        migrated.TryAdd(contextType, $"{kind} class '{type.Name}'");
                    }
                }
            },
            SymbolKind.NamedType);

        context.RegisterOperationAction(
            operationContext =>
            {
                var invocation = (IInvocationOperation)operationContext.Operation;
                var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
                if (EfQueryOperationAnalysis.GetInvocationSource(invocation) is not { } facade
                    || !SymbolEqualityComparer.Default.Equals(EfQueryOperationAnalysis.Unwrap(facade).Type, databaseFacade)
                    || EfQueryOperationAnalysis.Unwrap(facade) is not IPropertyReferenceOperation { Property.Name: "Database", Instance: { } instance }
                    || instance.Type is not { } contextType
                    || !EfQueryOperationAnalysis.IsOrDerivesFrom(contextType, dbContext))
                {
                    return;
                }

                switch (method.Name)
                {
                    case "Migrate" or "MigrateAsync":
                        migrated.TryAdd(contextType, $"call to '{invocation.Syntax}'");
                        break;
                    case "EnsureCreated" or "EnsureCreatedAsync"
                        when SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, databaseFacade)
                            && !OtherArmMigrates(invocation, contextType):
                        candidates.Add((invocation, contextType));
                        break;
                }
            },
            OperationKind.Invocation);

        // A provider switch such as `if (db.Database.IsSqlite()) EnsureCreated(); else Migrate();`
        // chooses one or the other on purpose, so the two never meet on one database.
        static bool OtherArmMigrates(IInvocationOperation ensureCreated, ITypeSymbol contextType)
        {
            IOperation child = ensureCreated;
            for (var parent = ensureCreated.Parent; parent is not null; child = parent, parent = parent.Parent)
            {
                if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
                {
                    return false;
                }

                if (parent is IConditionalOperation conditional && !ReferenceEquals(conditional.Condition, child))
                {
                    var other = ReferenceEquals(conditional.WhenTrue, child) ? conditional.WhenFalse : conditional.WhenTrue;
                    if (other is not null && other.DescendantsAndSelf().OfType<IInvocationOperation>().Any(candidate =>
                        EfQueryOperationAnalysis.NormalizeMethod(candidate.TargetMethod).Name is "Migrate" or "MigrateAsync"
                        && EfQueryOperationAnalysis.GetInvocationSource(candidate) is { } source
                        && EfQueryOperationAnalysis.Unwrap(source) is IPropertyReferenceOperation { Property.Name: "Database", Instance.Type: { } migrated }
                        && SymbolEqualityComparer.Default.Equals(migrated, contextType)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        context.RegisterCompilationEndAction(endContext =>
        {
            foreach (var (call, contextType) in candidates)
            {
                var evidence = migrated.FirstOrDefault(entry => contextType is INamedTypeSymbol named && EfQueryOperationAnalysis.IsOrDerivesFrom(named, (INamedTypeSymbol)entry.Key));
                if (evidence.Key is null)
                {
                    continue;
                }

                var properties = ImmutableDictionary<string, string?>.Empty
                    .Add(DiagnosticPropertyNames.Confidence, Confidence)
                    .Add(DiagnosticPropertyNames.Evidence, $"'{call.Syntax}' runs on a '{contextType.Name}', and this project manages '{evidence.Key.Name}' with migrations: the {evidence.Value}.")
                    .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
                    .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
                    .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

                endContext.ReportDiagnostic(EfDiagnostic.Create(Rule, call.Syntax.GetLocation(), properties, contextType.Name));
            }
        });
    }
}
