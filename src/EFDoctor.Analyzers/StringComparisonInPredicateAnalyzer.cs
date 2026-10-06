using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringComparisonInPredicateAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD022";
    public const string RuleTitle = "StringComparison is not translatable in a query predicate";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD022";
    public const string Impact = "EF Core cannot translate StringComparison overloads to SQL. Because client evaluation of predicates is disabled by default since EF Core 3.0, the query throws an exception when it executes rather than filtering in the database.";

    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string StringComparisonMetadataName = "System.StringComparison";
    private const string Message = "This string comparison uses a StringComparison overload that EF Core cannot translate to SQL";
    private const string Remediation = "Use a translatable comparison — a plain ==, StartsWith, EndsWith, or Contains without a StringComparison argument — or configure a case-insensitive database collation for the column. If the query is intentionally evaluated on the client (for example after AsEnumerable), suppress with a recorded reason.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Correctness",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports a StringComparison string overload used inside a proven EF Core query predicate, which EF Core cannot translate to SQL.",
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

        var queryable = context.Compilation.GetTypeByMetadataName(QueryableMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        var stringComparison = context.Compilation.GetTypeByMetadataName(StringComparisonMetadataName);
        if (queryable is null || dbSet is null || dbContext is null || efExtensions is null || stringComparison is null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, queryable, dbSet, dbContext, efExtensions, stringComparison),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol stringComparison)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsStringComparisonCall(invocation, stringComparison))
        {
            return;
        }

        // Walk up to the nearest enclosing lambda, then to the operator that consumes
        // it. The rule fires only when that operator is a Queryable operator whose
        // source is a proven EF DbSet, which excludes in-memory LINQ and non-EF sources.
        var lambda = FirstAncestor<IAnonymousFunctionOperation>(invocation);
        if (lambda is null)
        {
            return;
        }

        var operatorInvocation = FirstAncestor<IInvocationOperation>(lambda);
        if (operatorInvocation is null)
        {
            return;
        }

        var operatorMethod = EfQueryOperationAnalysis.NormalizeMethod(operatorInvocation.TargetMethod);
        if (!SymbolEqualityComparer.Default.Equals(operatorMethod.ContainingType.OriginalDefinition, queryable))
        {
            return;
        }

        if (!EfQueryOperationAnalysis.TryAnalyzeSource(operatorInvocation, queryable, dbSet, dbContext, efExtensions, out _))
        {
            return;
        }

        var methodDisplay = invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var evidence = $"Invocation resolves to '{methodDisplay}', which takes a System.StringComparison argument, inside a predicate of a query traced to an EF Core DbSet; EF Core cannot translate this overload to SQL.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties));
    }

    private static bool IsStringComparisonCall(IInvocationOperation invocation, INamedTypeSymbol stringComparison)
    {
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        return method.ContainingType.SpecialType == SpecialType.System_String
            && method.Parameters.Any(parameter =>
                SymbolEqualityComparer.Default.Equals(parameter.Type, stringComparison));
    }

    private static T? FirstAncestor<T>(IOperation operation)
        where T : class, IOperation
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
