using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

internal static class EfIncludePaths
{
    public static bool TryGetSelector(IInvocationOperation invocation, out IAnonymousFunctionOperation lambda)
    {
        lambda = null!;
        var argument = invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 1)?.Value;
        return argument is not null
            && EfQueryOperationAnalysis.TryGetLambda(argument, out lambda)
            && lambda.Symbol.Parameters.Length == 1;
    }

    // Recovers the source-rooted property path selected by an Include or ThenInclude lambda.
    // `isFiltered` reports whether filtered-include operators were stripped to reach it.
    public static bool TryGetSegments(
        IAnonymousFunctionOperation lambda,
        out ImmutableArray<IPropertySymbol> segments,
        out bool isFiltered)
    {
        segments = ImmutableArray<IPropertySymbol>.Empty;
        isFiltered = false;
        var body = EfQueryOperationAnalysis.GetLambdaExpression(lambda);
        if (body is null)
        {
            return false;
        }

        // Filtered includes compose collection operators over the navigation, for example
        // `parent => parent.Children.Where(...).OrderBy(...)`; the navigation is their innermost source.
        var current = EfQueryOperationAnalysis.Unwrap(body);
        while (current is IInvocationOperation filter
            && EfQueryOperationAnalysis.NormalizeMethod(filter.TargetMethod).ContainingNamespace.ToDisplayString() == "System.Linq"
            && EfQueryOperationAnalysis.GetInvocationSource(filter) is { } filterSource)
        {
            isFiltered = true;
            current = EfQueryOperationAnalysis.Unwrap(filterSource);
        }

        var builder = ImmutableArray.CreateBuilder<IPropertySymbol>();
        while (current is IPropertyReferenceOperation reference && reference.Instance is not null)
        {
            builder.Insert(0, reference.Property);
            current = EfQueryOperationAnalysis.Unwrap(reference.Instance);
        }

        var parameter = lambda.Symbol.Parameters[0];
        if (builder.Count == 0
            || current is not IParameterReferenceOperation parameterReference
            || !SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter))
        {
            return false;
        }

        segments = builder.ToImmutable();
        return true;
    }
}
