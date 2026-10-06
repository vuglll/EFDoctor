using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CountUsedForExistenceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD002";
    public const string RuleTitle = "Count used only to test existence";
    public const string Confidence = "high";
    public const string Impact = "Counting solely to test existence can require the database to process more matching rows than an existence query that can stop after the first match.";
    public const string DocumentationKey = "EFD002";

    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    public const string EntityLoadConfidence = "medium";
    public const string EntityLoadImpact = "Loading an entity to test existence reads and materializes every column of the row and adds it to the change tracker, where an existence query returns a single value. The saving is at most one row per call, so it matters most for wide entities and frequently executed code.";

    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string Message = "This EF Core {0} is used only to test existence and {1}";
    private const string CountSubject = "count";
    private const string CountConsequence = "may process more rows than an existence query";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports semantically resolved EF Core Count calls used only as existence tests, and FirstOrDefault calls on entity queries whose result is only checked for null.",
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
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (queryable is null || dbSet is null || efExtensions is null)
        {
            return;
        }

        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        context.RegisterOperationAction(
            operationContext =>
            {
                AnalyzeInvocation(operationContext, queryable, dbSet, efExtensions);
                if (dbContext is not null)
                {
                    AnalyzeEntityLoad(operationContext, queryable, dbSet, dbContext, efExtensions);
                }
            },
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol efExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = GetUnreducedMethod(invocation.TargetMethod);
        var isAsync = IsMethod(method, "CountAsync", efExtensions);
        if (!isAsync && !IsMethod(method, "Count", queryable))
        {
            return;
        }

        if (!isAsync)
        {
            var source = GetQuerySource(invocation);
            if (source is null || !IsEfQuerySource(source, queryable, dbSet, efExtensions))
            {
                return;
            }
        }

        if (!TryGetResult(invocation, isAsync, out var result))
        {
            return;
        }

        var methodDisplay = invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var replacement = isAsync ? "AnyAsync" : "Any";
        string evidence;
        string polarity;
        if (TryClassifyExistenceComparison(result, out var comparison, out var testsForEmpty))
        {
            evidence = $"Invocation resolves to '{methodDisplay}' and its result is compared as '{comparison}', which uses the count only to test existence.";
            polarity = testsForEmpty ? $"the negation of {replacement}" : replacement;
        }
        else if (EntityOverFetchAnalyzer.TryGetInitializedLocal(result, out var local)
            && TryCollectUses(local, result, ClassifyCountUse, out var uses))
        {
            // The count is followed through one local, and only when nothing reads it as a number.
            evidence = $"Invocation resolves to '{methodDisplay}' and its result is stored in local '{local.Name}', which this method only compares for existence: {Describe(uses)}.";
            polarity = uses.All(static use => use.Negative) ? $"the negation of {replacement}"
                : uses.Any(static use => use.Negative) ? $"{replacement}, negated where the code tests for no rows,"
                : replacement;
        }
        else
        {
            return;
        }

        var remediation = $"Use {polarity} for this existence test so the query can stop after the first match. Preserve the predicate by using the corresponding {replacement} predicate overload when one is present.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties, CountSubject, CountConsequence));
    }

    // FirstOrDefault on an entity query answers "does a row exist?" when its result is only
    // checked for null. A projected query is excluded: its result can be null while a row exists.
    private static void AnalyzeEntityLoad(
        OperationAnalysisContext context,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = GetUnreducedMethod(invocation.TargetMethod);
        var isAsync = IsMethod(method, "FirstOrDefaultAsync", efExtensions);
        if ((!isAsync && !IsMethod(method, "FirstOrDefault", queryable))
            || method.Parameters.Any(static parameter => parameter.Name == "defaultValue")
            || GetQuerySource(invocation) is not { } source
            || !EfQueryOperationAnalysis.TryAnalyzeSource(source, queryable, dbSet, dbContext, efExtensions, out var analysis)
            || analysis.Operations.Any(IsProjection)
            || !EntityOverFetchAnalyzer.TryGetEntityType(analysis.Origin, dbSet, out var entity)
            || !TryGetResult(invocation, isAsync, out var result)
            || !SymbolEqualityComparer.Default.Equals(isAsync ? (invocation.Type as INamedTypeSymbol)?.TypeArguments.FirstOrDefault() : invocation.Type, entity))
        {
            return;
        }

        var methodDisplay = invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        string evidence;
        if (ClassifyNullCheck(result) is { } check)
        {
            evidence = $"Invocation resolves to '{methodDisplay}', its source is traced to DbSet origin '{analysis.Origin.Syntax}', and its result is only checked for null: '{check.Text}'.";
        }
        else if (EntityOverFetchAnalyzer.TryGetInitializedLocal(result, out var local)
            && TryCollectUses(local, result, ClassifyNullCheck, out var uses))
        {
            evidence = $"Invocation resolves to '{methodDisplay}', its source is traced to DbSet origin '{analysis.Origin.Syntax}', and its result is stored in local '{local.Name}', which this method only checks for null: {Describe(uses)}.";
        }
        else
        {
            return;
        }

        var replacement = isAsync ? "AnyAsync" : "Any";
        var remediation = $"Use {replacement} with the same predicate, negated where the code tests for null, so the database answers with an existence query instead of returning a row. The saving is at most one row's columns, materialization, and change tracking. Keep the load when the entity must be tracked for later code, and suppress EFD002 with a recorded reason.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, EntityLoadConfidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, EntityLoadImpact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            properties,
            $"{invocation.TargetMethod.Name} result",
            "loads an entity where an existence query would do"));
    }

    // A Queryable operator that can change what the query returns, such as Select. A navigation
    // projected to the entity's own type, like `Select(e => e.Parent)`, keeps the element type
    // but can still be null while a row exists.
    private static bool IsProjection(string operation) =>
        operation.StartsWith("Queryable.", StringComparison.Ordinal)
        && !EfQueryOperationAnalysis.ElementPreservingQueryableMethods.Contains(operation.Substring("Queryable.".Length));

    // Every reference to the local in the method must be a use the classifier accepts, outside
    // lambdas and local functions, and there must be at least one. Any other reference, including
    // a write, means the value is needed for something else.
    private static bool TryCollectUses(ILocalSymbol local, IOperation value, Func<IOperation, Use?> classify, out List<Use> uses)
    {
        uses = new List<Use>();
        IOperation root = value;
        while (root.Parent is not null)
        {
            root = root.Parent;
        }

        foreach (var operation in root.Descendants())
        {
            if (operation is not ILocalReferenceOperation reference
                || !SymbolEqualityComparer.Default.Equals(reference.Local, local))
            {
                continue;
            }

            for (var ancestor = reference.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ancestor is IAnonymousFunctionOperation or ILocalFunctionOperation)
                {
                    return false;
                }
            }

            if (classify(reference) is not { } use)
            {
                return false;
            }

            uses.Add(use);
        }

        return uses.Count > 0;
    }

    private static string Describe(List<Use> uses) =>
        string.Join(", ", uses.Select(static use => $"'{use.Text}'"));

    private static Use? ClassifyCountUse(IOperation count) =>
        TryClassifyExistenceComparison(count, out _, out var testsForEmpty)
            ? new Use(ComparisonSyntax(count), testsForEmpty)
            : null;

    private static string ComparisonSyntax(IOperation operand)
    {
        var current = operand;
        while (current.Parent is not null and not IBinaryOperation)
        {
            current = current.Parent;
        }

        return (current.Parent ?? operand).Syntax.ToString();
    }

    // `value == null`, `value != null`, `value is null`, or `value is not null`.
    private static Use? ClassifyNullCheck(IOperation value)
    {
        var current = value;
        while (current.Parent is IParenthesizedOperation
            || (current.Parent is IConversionOperation { IsImplicit: true } conversion && conversion.Operand == current))
        {
            current = current.Parent;
        }

        switch (current.Parent)
        {
            // A record's synthesized equality operator is still a null check against null.
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } binary
                when binary.OperatorMethod is null or { IsImplicitlyDeclared: true }:
                var other = binary.LeftOperand == current ? binary.RightOperand : binary.LeftOperand;
                return UnwrapTransparentOperation(other).ConstantValue is { HasValue: true, Value: null }
                    ? new Use(binary.Syntax.ToString(), binary.OperatorKind == BinaryOperatorKind.Equals)
                    : null;

            case IIsPatternOperation pattern when pattern.Value == current:
                return pattern.Pattern switch
                {
                    IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: null } } => new Use(pattern.Syntax.ToString(), true),
                    INegatedPatternOperation { Pattern: IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: null } } } => new Use(pattern.Syntax.ToString(), false),
                    _ => null,
                };

            default:
                return null;
        }
    }

    // One use of a followed result: its source text, and whether it tests for "no row".
    private sealed class Use
    {
        public Use(string text, bool negative)
        {
            Text = text;
            Negative = negative;
        }

        public string Text { get; }

        public bool Negative { get; }
    }

    private static bool IsMethod(IMethodSymbol method, string name, INamedTypeSymbol containingType)
    {
        return method.Name == name
            && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, containingType);
    }

    private static IMethodSymbol GetUnreducedMethod(IMethodSymbol method)
    {
        return (method.ReducedFrom ?? method).OriginalDefinition;
    }

    private static IOperation? GetQuerySource(IInvocationOperation invocation)
    {
        if (invocation.Instance is not null)
        {
            return invocation.Instance;
        }

        return invocation.Arguments
            .FirstOrDefault(static argument => argument.Parameter?.Ordinal == 0)
            ?.Value;
    }

    private static bool IsEfQuerySource(
        IOperation operation,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol efExtensions)
    {
        operation = UnwrapTransparentOperation(operation);
        if (IsOrDerivesFrom(operation.Type, dbSet))
        {
            return true;
        }

        if (operation is not IInvocationOperation invocation)
        {
            return false;
        }

        var method = GetUnreducedMethod(invocation.TargetMethod);
        var containingType = method.ContainingType.OriginalDefinition;
        if (!SymbolEqualityComparer.Default.Equals(containingType, queryable)
            && !SymbolEqualityComparer.Default.Equals(containingType, efExtensions))
        {
            return false;
        }

        var source = GetQuerySource(invocation);
        return source is not null && IsEfQuerySource(source, queryable, dbSet, efExtensions);
    }

    private static bool IsOrDerivesFrom(ITypeSymbol? type, INamedTypeSymbol expectedDefinition)
    {
        for (var candidate = type as INamedTypeSymbol; candidate is not null; candidate = candidate.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, expectedDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static IOperation UnwrapTransparentOperation(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion when conversion.IsImplicit:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    // The value an invocation produces: through parentheses and implicit conversions, and for an
    // asynchronous operation through its await, which is required.
    private static bool TryGetResult(IInvocationOperation invocation, bool isAsync, out IOperation result)
    {
        IOperation current = invocation;
        var observedAwait = false;
        while (current.Parent is not null)
        {
            switch (current.Parent)
            {
                case IConversionOperation conversion when conversion.Operand == current && conversion.IsImplicit:
                    current = conversion;
                    continue;
                case IParenthesizedOperation parenthesized when parenthesized.Operand == current:
                    current = parenthesized;
                    continue;
                case IAwaitOperation awaitOperation when awaitOperation.Operation == current && isAsync && !observedAwait:
                    observedAwait = true;
                    current = awaitOperation;
                    continue;
            }

            break;
        }

        result = current;
        return !isAsync || observedAwait;
    }

    // Whether a count value is compared with a constant in a way that only tests existence:
    // `> 0`, `!= 0`, `>= 1`, or, for an empty set, `== 0`, `<= 0`, `< 1`, in either operand order.
    internal static bool TryClassifyExistenceComparison(IOperation count, out string comparison, out bool testsForEmpty)
    {
        var current = count;
        while (current.Parent is IParenthesizedOperation
            || (current.Parent is IConversionOperation { IsImplicit: true } conversion && conversion.Operand == current))
        {
            current = current.Parent;
        }

        comparison = string.Empty;
        testsForEmpty = false;
        if (current.Parent is not IBinaryOperation binary || binary.OperatorMethod is not null)
        {
            return false;
        }

        var countIsLeft = binary.LeftOperand == current;
        if (!countIsLeft && binary.RightOperand != current)
        {
            return false;
        }

        var constantOperand = countIsLeft ? binary.RightOperand : binary.LeftOperand;
        if (!TryGetIntegralConstant(constantOperand, out var boundary))
        {
            return false;
        }

        var operatorKind = countIsLeft ? binary.OperatorKind : Reverse(binary.OperatorKind);
        (comparison, testsForEmpty) = (operatorKind, boundary) switch
        {
            (BinaryOperatorKind.GreaterThan, 0) => ("count > 0", false),
            (BinaryOperatorKind.NotEquals, 0) => ("count != 0", false),
            (BinaryOperatorKind.GreaterThanOrEqual, 1) => ("count >= 1", false),
            (BinaryOperatorKind.Equals, 0) => ("count == 0", true),
            (BinaryOperatorKind.LessThanOrEqual, 0) => ("count <= 0", true),
            (BinaryOperatorKind.LessThan, 1) => ("count < 1", true),
            _ => (string.Empty, false),
        };
        return comparison.Length > 0;
    }

    private static BinaryOperatorKind Reverse(BinaryOperatorKind operatorKind)
    {
        return operatorKind switch
        {
            BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
            BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThanOrEqual,
            BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
            BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThanOrEqual,
            _ => operatorKind,
        };
    }

    private static bool TryGetIntegralConstant(IOperation operation, out long value)
    {
        operation = UnwrapTransparentOperation(operation);
        if (!operation.ConstantValue.HasValue)
        {
            value = default;
            return false;
        }

        switch (operation.ConstantValue.Value)
        {
            case sbyte number:
                value = number;
                return true;
            case byte number:
                value = number;
                return true;
            case short number:
                value = number;
                return true;
            case ushort number:
                value = number;
                return true;
            case int number:
                value = number;
                return true;
            case uint number:
                value = number;
                return true;
            case long number:
                value = number;
                return true;
            default:
                value = default;
                return false;
        }
    }
}
