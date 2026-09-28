using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnorderedPaginationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD014";
    public const string RuleTitle = "Pagination has no deterministic order";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD014";
    public const string Impact = "Paging without a stable order returns rows in an order the database does not guarantee and that can vary between executions, so pages may repeat or omit rows; the actual result depends on the provider and data.";

    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "This EF Core query pages with Skip and no preceding ordering operator, so paged results are non-deterministic";
    private const string Remediation = "Add a stable ordering — typically OrderBy on a unique or tie-broken key — before Skip/Take. If an intentionally unordered sample is required, suppress with a recorded reason.";

    private static readonly ImmutableHashSet<string> OrderingMethods =
        ImmutableHashSet.Create(StringComparer.Ordinal, "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending");

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Correctness",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports a proven EF Core query that pages with Skip without a preceding OrderBy, producing non-deterministic results.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        var queryable = context.Compilation.GetTypeByMetadataName(QueryableMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (queryable is null || dbSet is null || dbContext is null || efExtensions is null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, queryable, dbSet, dbContext, efExtensions),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsQueryablePaging(invocation, queryable))
        {
            return;
        }

        // Emit one finding per paged query: report only the outermost paging operator,
        // so `Skip(n).Take(m)` reports on the `Take` rather than twice.
        if (IsConsumedByOuterPagingOperator(invocation, queryable))
        {
            return;
        }

        if (!EfQueryOperationAnalysis.TryAnalyzeSource(invocation, queryable, dbSet, dbContext, efExtensions, out var analysis))
        {
            return;
        }

        // Pagination requires Skip: skipping rows is only meaningful over a defined
        // order, so it is the unambiguous non-deterministic-paging bug. A bare Take is
        // a top-N shape that is frequently intentional, so it is not reported.
        if (!analysis.Operations.Any(static operation => operation == "Queryable.Skip"))
        {
            return;
        }

        // Tracing from the paging operator only records operators that precede it, so a
        // recognized ordering anywhere in the traced chain means paging is ordered.
        if (analysis.Operations.Any(static operation => IsOrderingOperation(operation)))
        {
            return;
        }

        var chain = analysis.Operations.IsDefaultOrEmpty
            ? "direct EF query source"
            : string.Join(" -> ", analysis.Operations);
        var methodDisplay = invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var evidence = $"Invocation resolves to '{methodDisplay}', its source is traced to DbSet origin '{analysis.Origin.Syntax}', the inline query chain is '{chain}', and no ordering operator precedes the Skip paging operator.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties));
    }

    private static bool IsQueryablePaging(IInvocationOperation invocation, INamedTypeSymbol queryable)
    {
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        return method.Name is "Skip" or "Take"
            && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, queryable);
    }

    private static bool IsOrderingOperation(string operation) =>
        operation.StartsWith("Queryable.", StringComparison.Ordinal)
        && OrderingMethods.Contains(operation.Substring("Queryable.".Length));

    private static bool IsConsumedByOuterPagingOperator(IInvocationOperation invocation, INamedTypeSymbol queryable)
    {
        for (var current = invocation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IConversionOperation:
                case IParenthesizedOperation:
                case IArgumentOperation:
                    continue;
                case IInvocationOperation outer:
                    var source = EfQueryOperationAnalysis.GetInvocationSource(outer);
                    return IsQueryablePaging(outer, queryable)
                        && source is not null
                        && ReferenceEquals(EfQueryOperationAnalysis.Unwrap(source), invocation);
                default:
                    return false;
            }
        }

        return false;
    }
}
