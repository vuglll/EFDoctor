using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFDoctor.Analyzers;

/// <summary>
/// Single-column keys declared through fluent EF Core configuration in one compilation:
/// <c>HasKey</c>, <c>HasAlternateKey</c>, <c>HasForeignKey</c>, and <c>HasIndex(...).IsUnique()</c>.
/// The compilation is scanned once, on the first lookup.
/// </summary>
internal sealed class EfModelKeys
{
    private static readonly ImmutableHashSet<string> ConfigurationMethods =
        ImmutableHashSet.Create("HasKey", "HasAlternateKey", "HasForeignKey", "HasIndex");

    private readonly Lazy<ImmutableHashSet<(ITypeSymbol Entity, string Property)>> _keys;

    public EfModelKeys(Compilation compilation, CancellationToken cancellationToken)
    {
        _keys = new Lazy<ImmutableHashSet<(ITypeSymbol Entity, string Property)>>(
            () => Scan(compilation, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static ImmutableHashSet<(ITypeSymbol Entity, string Property)> Scan(Compilation compilation, CancellationToken cancellationToken)
    {
        var keys = ImmutableHashSet.CreateBuilder<(ITypeSymbol Entity, string Property)>(KeyComparer.Instance);
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = tree.GetRoot(cancellationToken)
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(static invocation => ConfigurationMethods.Contains(GetName(invocation) ?? string.Empty))
                .ToArray();
            if (candidates.Length == 0)
            {
                continue;
            }

            // The configuration lives outside the operation EFD005 analyzes, so it is read from
            // the other trees of the same compilation, once per compilation.
#pragma warning disable RS1030
            var model = compilation.GetSemanticModel(tree);
#pragma warning restore RS1030
            foreach (var invocation in candidates)
            {
                if (TryGetKey(invocation, model, cancellationToken, out var key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys.ToImmutable();
    }

    public bool IsKey(ITypeSymbol entityType, string propertyName)
    {
        var keys = _keys.Value;
        for (var type = entityType; type is not null; type = type.BaseType)
        {
            if (keys.Contains((type.OriginalDefinition, propertyName)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetKey(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out (ITypeSymbol Entity, string Property) key)
    {
        key = default;
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
            || !method.ContainingNamespace.ToDisplayString().StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || (method.Name == "HasIndex" && !IsFollowedByIsUnique(invocation, model, cancellationToken)))
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 1 && arguments[0].Expression is LambdaExpressionSyntax lambda)
        {
            return TryGetLambdaColumn(lambda, model, cancellationToken, out key);
        }

        if (arguments.Count != 1
            || model.GetConstantValue(arguments[0].Expression, cancellationToken).Value is not string name
            || GetStringFormEntity(method) is not { } entity)
        {
            return false;
        }

        key = (entity.OriginalDefinition, name);
        return true;
    }

    private static bool TryGetLambdaColumn(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken,
        out (ITypeSymbol Entity, string Property) key)
    {
        key = default;
        if (model.GetSymbolInfo(lambda, cancellationToken).Symbol is not IMethodSymbol { Parameters.Length: 1 } function)
        {
            return false;
        }

        var body = lambda.ExpressionBody;
        if (body is AnonymousObjectCreationExpressionSyntax { Initializers.Count: 1 } anonymous)
        {
            body = anonymous.Initializers[0].Expression;
        }

        if (body is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } access
            || model.GetSymbolInfo(receiver, cancellationToken).Symbol is not IParameterSymbol parameter
            || !SymbolEqualityComparer.Default.Equals(parameter, function.Parameters[0])
            || model.GetSymbolInfo(access, cancellationToken).Symbol is not IPropertySymbol property)
        {
            return false;
        }

        key = (parameter.Type.OriginalDefinition, property.Name);
        return true;
    }

    private static ITypeSymbol? GetStringFormEntity(IMethodSymbol method)
    {
        if (method.IsGenericMethod && method.TypeArguments.Length == 1)
        {
            return method.TypeArguments[0];
        }

        var builder = method.ContainingType;
        return builder.Name switch
        {
            "EntityTypeBuilder" when builder.TypeArguments.Length == 1 => builder.TypeArguments[0],
            "OwnedNavigationBuilder" when builder.TypeArguments.Length == 2 => builder.TypeArguments[1],
            "ReferenceCollectionBuilder" when builder.TypeArguments.Length == 2 => builder.TypeArguments[1],
            _ => null,
        };
    }

    // `HasIndex(...)` bounds rows only when a later call in the same fluent chain makes it unique.
    private static bool IsFollowedByIsUnique(
        InvocationExpressionSyntax index,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        SyntaxNode current = index;
        while (current.Parent is MemberAccessExpressionSyntax access
            && access.Expression == current
            && access.Parent is InvocationExpressionSyntax next)
        {
            if (access.Name.Identifier.ValueText == "IsUnique")
            {
                var arguments = next.ArgumentList.Arguments;
                return arguments.Count == 0
                    || model.GetConstantValue(arguments[0].Expression, cancellationToken).Value is true;
            }

            current = next;
        }

        return false;
    }

    private static string? GetName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            SimpleNameSyntax name => name.Identifier.ValueText,
            _ => null,
        };

    private sealed class KeyComparer : IEqualityComparer<(ITypeSymbol Entity, string Property)>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals((ITypeSymbol Entity, string Property) x, (ITypeSymbol Entity, string Property) y) =>
            SymbolEqualityComparer.Default.Equals(x.Entity, y.Entity)
            && string.Equals(x.Property, y.Property, StringComparison.Ordinal);

        public int GetHashCode((ITypeSymbol Entity, string Property) key) =>
            (SymbolEqualityComparer.Default.GetHashCode(key.Entity) * 397) ^ StringComparer.Ordinal.GetHashCode(key.Property);
    }
}
