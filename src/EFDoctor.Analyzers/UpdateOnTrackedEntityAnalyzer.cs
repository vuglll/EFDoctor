using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UpdateOnTrackedEntityAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD026";
    public const string RuleTitle = "Update on an entity the context already tracks";
    public const string Confidence = "advisory";
    public const string DocumentationKey = "EFD026";
    public const string Impact = "The context already tracks this entity and detects which properties changed. Update marks every property as modified, so SaveChanges writes every column instead of only the changed ones: more data written, more trigger and replication work, and concurrent updates to other columns overwritten.";
    public const string Remediation = "Remove the Update call: SaveChanges saves the changes to a tracked entity on its own. Keep Update for detached entities, such as ones built from a request or loaded on another context.";

    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "'{0}' is already tracked, so Update is unnecessary and marks every column modified";

    private static readonly ImmutableHashSet<string> SingleTerminals =
        ImmutableHashSet.Create(StringComparer.Ordinal, "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault");

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Reports DbContext.Update or DbSet.Update on an entity the same method loaded with tracking from the same DbContext instance.",
        helpLinkUri: EfHelpLinks.For(DiagnosticId));

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

        var enumerable = context.Compilation.GetTypeByMetadataName(EnumerableMetadataName);
        var queryable = context.Compilation.GetTypeByMetadataName(QueryableMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (enumerable is null || queryable is null || dbSet is null || dbContext is null || efExtensions is null)
        {
            return;
        }

        context.RegisterOperationBlockAction(blockContext =>
        {
            foreach (var root in blockContext.OperationBlocks)
            {
                var identity = new EfContextIdentity(root, dbContext);
                foreach (var update in root.Descendants().OfType<IInvocationOperation>())
                {
                    if (TryGetUpdate(update, identity, dbSet, dbContext, out var entity, out var contextKey, out var contextDisplay)
                        && TryFindTrackedLoad(update, entity, contextKey, identity, enumerable, queryable, dbSet, dbContext, efExtensions, out var load))
                    {
                        var evidence = $"Local '{entity.Name}' is loaded with tracking by '{load.Syntax}' on context '{contextDisplay}', and nothing before this Update clears the tracker or detaches it.";
                        var properties = ImmutableDictionary<string, string?>.Empty
                            .Add(DiagnosticPropertyNames.Confidence, Confidence)
                            .Add(DiagnosticPropertyNames.Evidence, evidence)
                            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
                            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
                            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

                        blockContext.ReportDiagnostic(EfDiagnostic.Create(Rule, update.Syntax.GetLocation(), properties, entity.Name));
                    }
                }
            }
        });
    }

    // `context.Update(entity)`, `context.Update<T>(entity)`, or `context.Set.Update(entity)`.
    private static bool TryGetUpdate(
        IInvocationOperation invocation,
        EfContextIdentity identity,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        out ILocalSymbol entity,
        out ISymbol contextKey,
        out string contextDisplay)
    {
        entity = null!;
        contextKey = null!;
        contextDisplay = null!;
        if (invocation.TargetMethod.Name != "Update" || invocation.Instance is not { } instance || invocation.Arguments.Length != 1)
        {
            return false;
        }

        IOperation? context = EfQueryOperationAnalysis.IsOrDerivesFrom(invocation.TargetMethod.ContainingType, dbContext)
            ? instance
            : EfQueryOperationAnalysis.IsOrDerivesFrom(invocation.TargetMethod.ContainingType, dbSet)
                ? identity.ContextOfDbSet(instance)
                : null;
        if (context is null
            || !identity.TryGetKey(context, out contextKey, out contextDisplay)
            || EfQueryOperationAnalysis.Unwrap(invocation.Arguments[0].Value) is not { } argument)
        {
            return false;
        }

        // `order!` and `(Order)order` read the same local.
        while (argument is IConversionOperation conversion)
        {
            argument = EfQueryOperationAnalysis.Unwrap(conversion.Operand);
        }

        if (argument is not ILocalReferenceOperation reference)
        {
            return false;
        }

        entity = reference.Local;
        return true;
    }

    private static bool TryFindTrackedLoad(
        IInvocationOperation update,
        ILocalSymbol entity,
        ISymbol contextKey,
        EfContextIdentity identity,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        out IInvocationOperation load)
    {
        load = null!;
        IOperation child = update;
        for (var parent = update.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return false;
            }

            if (parent is not IBlockOperation block)
            {
                continue;
            }

            var updateIndex = block.Operations.IndexOf(child);
            for (var index = updateIndex - 1; index >= 0; index--)
            {
                if (block.Operations[index] is not IVariableDeclarationGroupOperation group
                    || group.Declarations.SelectMany(static declaration => declaration.Declarators).FirstOrDefault(declarator => SymbolEqualityComparer.Default.Equals(declarator.Symbol, entity)) is not { } declarator)
                {
                    continue;
                }

                if (declarator.Initializer?.Value is not { } initializer
                    || !IsTrackedLoad(initializer, contextKey, identity, enumerable, queryable, dbSet, dbContext, efExtensions, out load))
                {
                    return false;
                }

                var between = block.Operations.Skip(index + 1).Take(updateIndex - index - 1).SelectMany(static statement => statement.DescendantsAndSelf());
                return !between.Any(operation => ChangesTracking(operation, entity));
            }
        }

        return false;
    }

    private static bool IsTrackedLoad(
        IOperation value,
        ISymbol contextKey,
        EfContextIdentity identity,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        out IInvocationOperation load)
    {
        load = null!;
        value = EfQueryOperationAnalysis.Unwrap(value);
        if (value is IAwaitOperation awaited)
        {
            value = EfQueryOperationAnalysis.Unwrap(awaited.Operation);
        }

        // `FindAsync` returns a ValueTask; `.AsTask()` or a conversion can wrap it.
        if (value is IInvocationOperation { TargetMethod.Name: "AsTask", Instance: { } task })
        {
            value = EfQueryOperationAnalysis.Unwrap(task);
        }

        if (value is not IInvocationOperation invocation)
        {
            return false;
        }

        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        if (method.Name is "Find" or "FindAsync"
            && invocation.Instance is { } set
            && EfQueryOperationAnalysis.IsOrDerivesFrom(method.ContainingType, dbSet))
        {
            load = invocation;
            return identity.TryGetKey(identity.ContextOfDbSet(set), out var findKey, out _) && SymbolEqualityComparer.Default.Equals(findKey, contextKey);
        }

        var containingType = method.ContainingType.OriginalDefinition;
        var name = method.Name;
        if (SymbolEqualityComparer.Default.Equals(containingType, efExtensions) && name.EndsWith("Async", StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - "Async".Length);
        }
        else if (!SymbolEqualityComparer.Default.Equals(containingType, queryable) && !SymbolEqualityComparer.Default.Equals(containingType, enumerable))
        {
            return false;
        }

        if (!SingleTerminals.Contains(name)
            || EfQueryOperationAnalysis.GetInvocationSource(invocation) is not { } source
            || !EfQueryOperationAnalysis.TryAnalyzeSource(source, queryable, dbSet, dbContext, efExtensions, out var analysis)
            || !identity.TryGetKey(identity.ContextOfDbSet(analysis.Origin), out var loadKey, out _)
            || !SymbolEqualityComparer.Default.Equals(loadKey, contextKey))
        {
            return false;
        }

        // A projection builds new instances, even of the entity type, which the context doesn't track.
        if (analysis.Operations.Any(static operation => operation is "Queryable.Select" or "Queryable.SelectMany" or "Queryable.GroupBy" or "Queryable.Join" or "Queryable.GroupJoin"))
        {
            return false;
        }

        var tracking = analysis.Operations.LastOrDefault(static operation => operation is "EF.AsNoTracking" or "EF.AsNoTrackingWithIdentityResolution" or "EF.AsTracking");
        if (tracking is "EF.AsNoTracking" or "EF.AsNoTrackingWithIdentityResolution")
        {
            return false;
        }

        load = invocation;
        return true;
    }

    // ChangeTracker.Clear(), an Entry(…).State assignment, or a write to the entity local.
    private static bool ChangesTracking(IOperation operation, ILocalSymbol entity) => operation switch
    {
        IInvocationOperation { TargetMethod.Name: "Clear", Instance: IPropertyReferenceOperation { Property.Name: "ChangeTracker" } } => true,
        ISimpleAssignmentOperation { Target: IPropertyReferenceOperation { Property.Name: "State", Instance: IInvocationOperation { TargetMethod.Name: "Entry" } } } => true,
        ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } when SymbolEqualityComparer.Default.Equals(target.Local, entity) => true,
        _ => false,
    };
}
