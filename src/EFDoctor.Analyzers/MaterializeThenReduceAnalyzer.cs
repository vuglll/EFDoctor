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
    private const string Message = "This EF Core query is fully materialized and then {0}";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports proven EF Core queries materialized with ToList or ToArray and immediately reduced on the client to an element, count, existence check, or aggregate, or stored in a local that is used only for its count.",
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
        var evidence = reduction.StoredLocal is null
            ? $"Invocation resolves to '{methodDisplay}', its source is traced to DbSet origin '{queryAnalysis.Origin.Syntax}', and the buffered result is immediately reduced by {reduction.Display}."
            : $"Invocation resolves to '{methodDisplay}', its source is traced to DbSet origin '{queryAnalysis.Origin.Syntax}', and the buffered result is stored in local '{reduction.StoredLocal}', which this method uses only for its count: {reduction.Display}.";
        if (reduction.Comparison is not null)
        {
            evidence += $" The count is compared as '{reduction.Comparison}', which only tests existence.";
        }

        var impact = $"Materializing first transfers and buffers every row the query returns, and tracks them for entity queries, only to reduce them on the client to {reduction.Result}; the database could compute that result directly and return far less data. Actual impact grows with the number of rows the query returns.";
        var negation = reduction.Negated ? "the negation of " : string.Empty;
        var remediation = asynchronous
            ? $"Replace the awaited materialization with {negation}the EF Core asynchronous reducer '{reduction.QueryOperator}Async' on the query so the reduction is translated into SQL, and verify semantics such as ordering and string comparison in the generated SQL. Keep the materialized list only when its rows are also needed elsewhere."
            : $"Apply {negation}'{reduction.QueryOperator}' to the query before materialization (or use the EF Core asynchronous reducer '{reduction.QueryOperator}Async') so the reduction is translated into SQL, and verify semantics such as ordering and string comparison in the generated SQL. Keep the materialized list only when its rows are also needed elsewhere.";
        if (reduction.Comparison is not null)
        {
            remediation += " An existence query can stop at the first matching row; keep any predicate as the Any predicate.";
        }

        if (reduction.NegatedWhereEmpty)
        {
            remediation += " Negate it where the code tests for an empty result.";
        }

        if (reduction.Reads > 1)
        {
            remediation += $" The local is read {reduction.Reads} times, so run the query-side operator once and reuse its value.";
        }

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, materializer.Syntax.GetLocation(), properties, reduction.MessageTail));
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

        if (EntityOverFetchAnalyzer.TryGetInitializedLocal(value, out var local))
        {
            return TryGetStoredCountReduction(local, value, enumerable, out reduction);
        }

        if (value.Parent is IPropertyReferenceOperation property && ReferenceEquals(property.Instance, value))
        {
            if (property.Property.Name == "Count"
                && property.Property.ContainingType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
            {
                reduction = Reduction.Count("the 'List<T>.Count' property", "Count", property);
                return true;
            }

            if (property.Property.Name == "Length" && value.Type is IArrayTypeSymbol)
            {
                reduction = Reduction.Count("the array 'Length' property", "Count", property);
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

        reduction = result == "a count"
            ? Reduction.Count($"'Enumerable.{method.Name}'", method.Name, invocation)
            : new Reduction($"'Enumerable.{method.Name}'", method.Name, result);
        return true;
    }

    // A materialized result stored in a local is reported only when nothing in the method could
    // need its rows: every reference to the local reads its count, or asks whether it has any
    // element, outside lambdas and local functions.
    private static bool TryGetStoredCountReduction(ILocalSymbol local, IOperation value, INamedTypeSymbol enumerable, out Reduction reduction)
    {
        reduction = null!;
        IOperation root = value;
        while (root.Parent is not null)
        {
            root = root.Parent;
        }

        var reads = new List<string>();
        var onlyExistence = true;
        var testsForEmpty = false;
        foreach (var operation in root.Descendants())
        {
            if (operation is not ILocalReferenceOperation reference
                || !SymbolEqualityComparer.Default.Equals(reference.Local, local))
            {
                continue;
            }

            if (!TryClassifyCountRead(reference, enumerable, out var read, out var testsExistence, out var emptyTest))
            {
                return false;
            }

            reads.Add(read);
            onlyExistence &= testsExistence;
            testsForEmpty |= emptyTest;
        }

        if (reads.Count == 0)
        {
            return false;
        }

        reduction = new Reduction(
            string.Join(", ", reads.Select(static read => $"'{read}'")),
            onlyExistence ? "Any" : "Count",
            onlyExistence ? "a boolean" : "a count",
            messageTail: $"used only for its count through local '{local.Name}'",
            storedLocal: local.Name,
            reads: reads.Count,
            negatedWhereEmpty: onlyExistence && testsForEmpty);
        return true;
    }

    private static bool TryClassifyCountRead(ILocalReferenceOperation reference, INamedTypeSymbol enumerable, out string read, out bool testsExistence, out bool testsForEmpty)
    {
        read = null!;
        testsExistence = false;
        testsForEmpty = false;
        for (var ancestor = reference.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return false;
            }
        }

        IOperation current = reference;
        while (current.Parent is IParenthesizedOperation
            || (current.Parent is IConversionOperation { IsImplicit: true } conversion && conversion.Operand == current))
        {
            current = current.Parent;
        }

        IOperation count;
        switch (current.Parent)
        {
            case IPropertyReferenceOperation { Property.Name: "Count" or "Length", Arguments.Length: 0 } property when property.Instance == current:
                count = property;
                break;

            case IArgumentOperation { Parameter.Ordinal: 0, Parent: IInvocationOperation { Arguments.Length: 1 } invocation }
                when SymbolEqualityComparer.Default.Equals(EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod).ContainingType.OriginalDefinition, enumerable)
                    && invocation.TargetMethod.Name is "Any" or "Count" or "LongCount":
                if (invocation.TargetMethod.Name == "Any")
                {
                    read = invocation.Syntax.ToString();
                    testsExistence = true;
                    return true;
                }

                count = invocation;
                break;

            default:
                return false;
        }

        testsExistence = CountUsedForExistenceAnalyzer.TryClassifyExistenceComparison(count, out _, out testsForEmpty);
        read = testsExistence ? ComparisonSyntax(count) : count.Syntax.ToString();
        return true;
    }

    // The source text of the comparison a count takes part in, such as `products.Count > 0`.
    private static string ComparisonSyntax(IOperation count)
    {
        var current = count;
        while (current.Parent is not null and not IBinaryOperation)
        {
            current = current.Parent;
        }

        return (current.Parent ?? count).Syntax.ToString();
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
        public Reduction(
            string display,
            string queryOperator,
            string result,
            string? messageTail = null,
            string? comparison = null,
            bool negated = false,
            string? storedLocal = null,
            int reads = 1,
            bool negatedWhereEmpty = false)
        {
            Display = display;
            QueryOperator = queryOperator;
            Result = result;
            MessageTail = messageTail ?? "immediately reduced by " + display;
            Comparison = comparison;
            Negated = negated;
            StoredLocal = storedLocal;
            Reads = reads;
            NegatedWhereEmpty = negatedWhereEmpty;
        }

        // A count reducer whose value is only compared for existence is better served by Any.
        public static Reduction Count(string display, string countOperator, IOperation count) =>
            CountUsedForExistenceAnalyzer.TryClassifyExistenceComparison(count, out var comparison, out var testsForEmpty)
                ? new Reduction(display, "Any", "a boolean", comparison: comparison, negated: testsForEmpty)
                : new Reduction(display, countOperator, "a count");

        public string Display { get; }

        public string QueryOperator { get; }

        public string Result { get; }

        public string MessageTail { get; }

        public string? Comparison { get; }

        public bool Negated { get; }

        public string? StoredLocal { get; }

        public int Reads { get; }

        public bool NegatedWhereEmpty { get; }
    }
}
