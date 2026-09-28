using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MaterializeThenReduceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD019";
    public const string RuleTitle = "Query is materialized and then immediately reduced";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD019";

    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "This EF Core query is fully materialized and then immediately reduced by {0}";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports proven EF Core queries materialized with ToList or ToArray and immediately reduced on the client to an element, count, existence check, or aggregate.");

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

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, enumerable, queryable, dbSet, dbContext, efExtensions),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions)
    {
        var materializer = (IInvocationOperation)context.Operation;
        if (!PrematureQueryMaterializationAnalyzer.TryClassifyMaterializer(materializer, enumerable, efExtensions, out var asynchronous))
        {
            return;
        }

        var source = EfQueryOperationAnalysis.GetInvocationSource(materializer);
        if (source is null
            || !EfQueryOperationAnalysis.TryAnalyzeSource(source, queryable, dbSet, dbContext, efExtensions, out var queryAnalysis)
            || !TryGetEligibleReducer(materializer, asynchronous, enumerable, queryAnalysis, out var reduction))
        {
            return;
        }

        var methodDisplay = materializer.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var evidence = $"Invocation resolves to '{methodDisplay}', its source is traced to DbSet origin '{queryAnalysis.Origin.Syntax}', and the buffered result is immediately reduced by {reduction.Display}.";
        var impact = $"Materializing first transfers and buffers every row the query returns, and tracks them for entity queries, only to reduce them on the client to {reduction.Result}; the database could compute that result directly and return far less data. Actual impact grows with the number of rows the query returns.";
        var remediation = asynchronous
            ? $"Replace the awaited materialization with the EF Core asynchronous reducer '{reduction.QueryOperator}Async' on the query so the reduction is translated into SQL, and verify semantics such as ordering and string comparison in the generated SQL. Keep the materialized list only when its rows are also needed elsewhere."
            : $"Apply '{reduction.QueryOperator}' to the query before materialization (or use the EF Core asynchronous reducer '{reduction.QueryOperator}Async') so the reduction is translated into SQL, and verify semantics such as ordering and string comparison in the generated SQL. Keep the materialized list only when its rows are also needed elsewhere.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(Diagnostic.Create(Rule, materializer.Syntax.GetLocation(), properties, reduction.Display));
    }

    internal static bool TryGetEligibleReducer(
        IInvocationOperation materializer,
        bool asynchronous,
        INamedTypeSymbol enumerable,
        EfQueryChainAnalysis queryAnalysis,
        out Reduction reduction)
    {
        reduction = null!;
        if (!PrematureQueryMaterializationAnalyzer.TryGetMaterializedValue(materializer, asynchronous, out var value))
        {
            return false;
        }

        if (value.Parent is IPropertyReferenceOperation property && ReferenceEquals(property.Instance, value))
        {
            if (property.Property.Name == "Count"
                && property.Property.ContainingType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
            {
                reduction = new Reduction("the 'List<T>.Count' property", "Count", "a count");
                return true;
            }

            if (property.Property.Name == "Length" && value.Type is IArrayTypeSymbol)
            {
                reduction = new Reduction("the array 'Length' property", "Count", "a count");
                return true;
            }

            return false;
        }

        IInvocationOperation? invocation = null;
        if (value.Parent is IInvocationOperation directInvocation && ReferenceEquals(directInvocation.Instance, value))
        {
            invocation = directInvocation;
        }
        else if (value.Parent is IArgumentOperation { Parameter.Ordinal: 0 } argument
            && argument.Parent is IInvocationOperation argumentInvocation)
        {
            invocation = argumentInvocation;
        }

        if (invocation is null)
        {
            return false;
        }

        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        if (!SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, enumerable))
        {
            return false;
        }

        var arguments = invocation.Arguments
            .Where(static argument => argument.Parameter?.Ordinal != 0)
            .OrderBy(static argument => argument.Parameter?.Ordinal)
            .Select(static argument => argument.Value)
            .ToArray();
        var result = method.Name switch
        {
            "First" or "FirstOrDefault" or "Single" or "SingleOrDefault" when IsNoneOrPredicate(arguments) => "a single element",
            "Last" or "LastOrDefault" when IsNoneOrPredicate(arguments) && IsOrdered(queryAnalysis) => "a single element",
            "Any" when IsNoneOrPredicate(arguments) => "a boolean",
            "All" when arguments.Length == 1 && IsPredicate(arguments[0]) => "a boolean",
            "Count" or "LongCount" when IsNoneOrPredicate(arguments) => "a count",
            "Sum" or "Min" or "Max" or "Average" when IsAggregateArgument(arguments, invocation.Type) => "an aggregate value",
            _ => null,
        };
        if (result is null)
        {
            return false;
        }

        reduction = new Reduction($"'Enumerable.{method.Name}'", method.Name, result);
        return true;
    }

    private static bool IsNoneOrPredicate(IOperation[] arguments) =>
        arguments.Length == 0 || (arguments.Length == 1 && IsPredicate(arguments[0]));

    private static bool IsPredicate(IOperation argument) =>
        EfQueryOperationAnalysis.TryGetLambda(argument, out var lambda)
        && lambda.Symbol.Parameters.Length == 1
        && EfQueryOperationAnalysis.IsPredicateExpression(EfQueryOperationAnalysis.GetLambdaExpression(lambda), lambda.Symbol.Parameters[0]);

    // A parameterless aggregate must run over scalar elements (for example a projected numeric
    // column); a selector must be a direct entity property path so the aggregate can translate.
    private static bool IsAggregateArgument(IOperation[] arguments, ITypeSymbol? resultType)
    {
        if (arguments.Length == 0)
        {
            return resultType is not null
                && (resultType.IsValueType || resultType.SpecialType == SpecialType.System_String);
        }

        return arguments.Length == 1
            && EfQueryOperationAnalysis.TryGetLambda(arguments[0], out var lambda)
            && lambda.Symbol.Parameters.Length == 1
            && EfQueryOperationAnalysis.IsEntityPropertyPath(EfQueryOperationAnalysis.GetLambdaExpression(lambda), lambda.Symbol.Parameters[0]);
    }

    // EF Core cannot translate Last or LastOrDefault over an unordered query.
    private static bool IsOrdered(EfQueryChainAnalysis queryAnalysis) =>
        queryAnalysis.Operations.Any(static operation => operation is "Queryable.OrderBy" or "Queryable.OrderByDescending");

    internal sealed class Reduction
    {
        public Reduction(string display, string queryOperator, string result)
        {
            Display = display;
            QueryOperator = queryOperator;
            Result = result;
        }

        public string Display { get; }

        public string QueryOperator { get; }

        public string Result { get; }
    }
}
