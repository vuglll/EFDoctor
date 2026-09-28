using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnboundedQueryMaterializationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD005";
    public const string RuleTitle = "Query materialization has no recognized row bound";
    public const string Confidence = "medium";
    public const string DocumentationKey = "EFD005";
    public const string Impact = "A query without a recognized strong row bound may transfer and buffer more rows than expected and increase database work, memory use, or latency; actual impact depends on data size and workload intent.";

    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "This EF Core query is materialized without a recognized query-side row bound";
    private const string Remediation = "If this result set can grow without bound and you do not need every row, consider server-side paging with stable ordering, chunked processing, or a purpose-built aggregate. If you need every matching row — for example hydrating children for a known set of parents — full materialization is expected. Exports, batch processing, migrations, cache warm-up, and known-small lookup tables can also be legitimate; suppress with a recorded reason.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports proven EF Core list materialization whose proven query has no recognized strong row bound.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        var enumerable = context.Compilation.GetTypeByMetadataName(EnumerableMetadataName);
        var queryable = context.Compilation.GetTypeByMetadataName(QueryableMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (enumerable is null || queryable is null || dbSet is null || dbContext is null || efExtensions is null)
        {
            return;
        }

        // Fluent key configuration is read from the whole compilation, at most once and only
        // when a key equality needs it.
        var modelKeys = new EfModelKeys(context.Compilation, context.CancellationToken);

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(
                operationContext,
                enumerable,
                queryable,
                dbSet,
                dbContext,
                efExtensions,
                modelKeys),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        EfModelKeys modelKeys)
    {
        var materializer = (IInvocationOperation)context.Operation;
        if (!TryClassifyMaterializer(materializer, enumerable, efExtensions, out var asynchronous))
        {
            return;
        }

        var source = EfQueryOperationAnalysis.GetInvocationSource(materializer);
        if (source is null
            || !EfQueryOperationAnalysis.TryAnalyzeSource(
                source,
                queryable,
                dbSet,
                dbContext,
                efExtensions,
                out var queryAnalysis,
                modelKeys)
            || queryAnalysis.RowBound.Strength == RowBoundStrength.Strong)
        {
            return;
        }

        // EFD004 and EFD019 give the more specific explanation when the buffered result is
        // immediately composed or reduced, so EFD005 yields to them at the same materializer.
        if (PrematureQueryMaterializationAnalyzer.TryGetEligibleConsumer(
                materializer,
                asynchronous,
                enumerable,
                out _)
            || MaterializeThenReduceAnalyzer.TryGetEligibleReducer(
                materializer,
                asynchronous,
                enumerable,
                queryAnalysis,
                out _))
        {
            return;
        }

        var methodDisplay = materializer.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var chain = queryAnalysis.Operations.IsDefaultOrEmpty
            ? "direct EF query source"
            : string.Join(" -> ", queryAnalysis.Operations);
        var confidence = GetConfidence(queryAnalysis, context.ContainingSymbol);
        var evidence = $"Invocation resolves to '{methodDisplay}', its source is traced to DbSet origin '{queryAnalysis.Origin.Syntax}', the inline query chain is '{chain}', and the recognized row bound is {queryAnalysis.RowBound.Kind} ({queryAnalysis.RowBound.Strength}).";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(Diagnostic.Create(Rule, materializer.Syntax.GetLocation(), properties));
    }

    private static string GetConfidence(EfQueryChainAnalysis queryAnalysis, ISymbol containingSymbol)
    {
        // A key-by-name equality (medium) most likely loads one row or the children of one
        // parent, and a time window (weak) signals intent, so both stay visible as advisory.
        if (queryAnalysis.RowBound.Strength is RowBoundStrength.Weak or RowBoundStrength.Medium)
        {
            return "advisory";
        }

        return queryAnalysis.RowBound.Kind == RowBoundKind.None
            && IsHotPath(containingSymbol)
            && !IsKnownSmallLookup(queryAnalysis.Origin.Type)
                ? "high"
                : Confidence;
    }

    private static bool IsHotPath(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is INamedTypeSymbol type
                && (type.Name.EndsWith("Handler", StringComparison.Ordinal)
                    || type.Name.EndsWith("Controller", StringComparison.Ordinal)
                    || type.Name.EndsWith("Service", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownSmallLookup(ITypeSymbol? sourceType)
    {
        if (sourceType is not INamedTypeSymbol named || named.TypeArguments.Length != 1)
        {
            return false;
        }

        var entityName = named.TypeArguments[0].Name;
        return entityName.EndsWith("Lookup", StringComparison.Ordinal)
            || entityName.EndsWith("Status", StringComparison.Ordinal)
            || entityName.EndsWith("Type", StringComparison.Ordinal)
            || entityName is "Country" or "Currency";
    }

    private static bool TryClassifyMaterializer(
        IInvocationOperation invocation,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol efExtensions,
        out bool asynchronous)
    {
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        var containingType = method.ContainingType.OriginalDefinition;
        asynchronous = false;
        if (method.Name == "ToList"
            && SymbolEqualityComparer.Default.Equals(containingType, enumerable))
        {
            return true;
        }

        if (method.Name == "ToListAsync"
            && SymbolEqualityComparer.Default.Equals(containingType, efExtensions))
        {
            asynchronous = true;
            return true;
        }

        return false;
    }
}
