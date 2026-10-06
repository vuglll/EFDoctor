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
    private const string Message = "This EF Core count is used only to test existence and may process more rows than an existence query";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports semantically resolved EF Core Count calls used only as direct existence tests.",
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

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, queryable, dbSet, efExtensions),
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

        if (!TryClassifyComparison(invocation, isAsync, out var comparison, out var testsForEmpty))
        {
            return;
        }

        var methodDisplay = invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var replacement = isAsync ? "AnyAsync" : "Any";
        var polarity = testsForEmpty ? $"the negation of {replacement}" : replacement;
        var remediation = $"Use {polarity} for this existence test so the query can stop after the first match. Preserve the predicate by using the corresponding {replacement} predicate overload when one is present.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, $"Invocation resolves to '{methodDisplay}' and its result is compared as '{comparison}', which uses the count only to test existence.")
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties));
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

    private static bool TryClassifyComparison(
        IInvocationOperation invocation,
        bool isAsync,
        out string comparison,
        out bool testsForEmpty)
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

        comparison = string.Empty;
        testsForEmpty = false;
        if (isAsync && !observedAwait)
        {
            return false;
        }

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
