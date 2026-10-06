using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

internal enum RowBoundKind
{
    None,
    TimeWindow,
    KeyEqualityByName,
    KeyEquality,
    LocalCollectionMembership,
    Take,
}

internal enum RowBoundStrength
{
    None,
    Weak,
    Medium,
    Strong,
}

internal readonly struct RowBound
{
    public RowBound(RowBoundKind kind, RowBoundStrength strength)
    {
        Kind = kind;
        Strength = strength;
    }

    public RowBoundKind Kind { get; }

    public RowBoundStrength Strength { get; }

    public static RowBound None => new(RowBoundKind.None, RowBoundStrength.None);
}

internal sealed class EfQueryChainAnalysis
{
    public EfQueryChainAnalysis(IOperation origin, ImmutableArray<string> operations, RowBound rowBound)
    {
        Origin = origin;
        Operations = operations;
        RowBound = rowBound;
    }

    public IOperation Origin { get; }

    public ImmutableArray<string> Operations { get; }

    public RowBound RowBound { get; }
}

internal static class EfQueryOperationAnalysis
{
    // Query-composition methods that keep the query element type and its include state, so an
    // inline include chain can be followed through them.
    public static readonly ImmutableHashSet<string> ElementPreservingQueryableMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Where",
            "OrderBy",
            "OrderByDescending",
            "ThenBy",
            "ThenByDescending",
            "Skip",
            "Take",
            "Distinct");

    public static readonly ImmutableHashSet<string> ElementPreservingEfMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Include",
            "ThenInclude",
            "AsNoTracking",
            "AsNoTrackingWithIdentityResolution",
            "AsTracking",
            "IgnoreAutoIncludes",
            "IgnoreQueryFilters",
            "TagWith",
            "TagWithCallSite");

    public static readonly ImmutableHashSet<string> ElementPreservingRelationalMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "AsSplitQuery",
            "AsSingleQuery");

    // EF composition the inline chain proof passes through. It must accept every EF method in
    // ElementPreservingEfMethods, or queries using them silently escape every rule built on the proof.
    internal static readonly ImmutableHashSet<string> BoundNeutralEfMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "AsSplitQuery",
            "AsSingleQuery",
            "AsNoTracking",
            "AsNoTrackingWithIdentityResolution",
            "AsTracking",
            "IgnoreAutoIncludes",
            "IgnoreQueryFilters",
            "Include",
            "ThenInclude",
            "TagWith",
            "TagWithCallSite");

    public static IMethodSymbol NormalizeMethod(IMethodSymbol method) =>
        (method.ReducedFrom ?? method).OriginalDefinition;

    public static IOperation? GetInvocationSource(IInvocationOperation invocation) =>
        invocation.Instance
        ?? invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 0)?.Value;

    public static bool TryAnalyzeSource(
        IOperation operation,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        out EfQueryChainAnalysis analysis,
        EfModelKeys? modelKeys = null)
    {
        var operations = ImmutableArray.CreateBuilder<string>();
        if (TryAnalyzeSourceCore(
            operation,
            queryable,
            dbSet,
            dbContext,
            efExtensions,
            modelKeys,
            operations,
            localHops: 0,
            out var origin,
            out var rowBound))
        {
            analysis = new EfQueryChainAnalysis(origin, operations.ToImmutable(), rowBound);
            return true;
        }

        analysis = null!;
        return false;
    }

    // OperationKind.CollectionExpression, which the Roslyn baseline the analyzers compile against
    // doesn't declare yet.
    public const int CollectionExpressionOperationKind = 0x7f;

    public static IOperation Unwrap(IOperation operation)
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

    // Treats `nullable.Value` and `nullable.GetValueOrDefault()` as transparent so a
    // null-guarded key path such as `x.ProductId.Value` resolves to entity property
    // `ProductId` rather than the `Nullable<T>` member.
    private static IOperation UnwrapNullableAccess(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IPropertyReferenceOperation reference
                    when reference.Instance is not null
                        && reference.Property.Name == "Value"
                        && reference.Property.ContainingType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T:
                    operation = Unwrap(reference.Instance);
                    continue;
                case IInvocationOperation invocation
                    when invocation.Instance is not null
                        && invocation.TargetMethod.Name == "GetValueOrDefault"
                        && invocation.TargetMethod.ContainingType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T:
                    operation = Unwrap(invocation.Instance);
                    continue;
                default:
                    return operation;
            }
        }
    }

    public static bool IsOrDerivesFrom(ITypeSymbol? type, INamedTypeSymbol expected)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, expected))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsPredicateExpression(IOperation? operation, IParameterSymbol entityParameter)
    {
        if (operation is null)
        {
            return false;
        }

        operation = Unwrap(operation);
        if (operation is IUnaryOperation unary)
        {
            return unary.OperatorMethod is null
                && unary.OperatorKind == UnaryOperatorKind.Not
                && IsPredicateExpression(unary.Operand, entityParameter);
        }

        if (operation is IBinaryOperation binary)
        {
            if (binary.OperatorMethod is not null)
            {
                return false;
            }

            if (binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr)
            {
                return IsPredicateExpression(binary.LeftOperand, entityParameter)
                    && IsPredicateExpression(binary.RightOperand, entityParameter);
            }

            if (binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
                or BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual
                or BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual)
            {
                return IsScalar(binary.LeftOperand, entityParameter) && IsScalar(binary.RightOperand, entityParameter);
            }
        }

        return IsEntityPropertyPath(operation, entityParameter)
            && operation.Type?.SpecialType == SpecialType.System_Boolean;
    }

    public static bool IsScalar(IOperation operation, IParameterSymbol entityParameter)
    {
        operation = Unwrap(operation);
        if (IsEntityPropertyPath(operation, entityParameter) || operation.ConstantValue.HasValue)
        {
            return true;
        }

        return operation switch
        {
            ILocalReferenceOperation => true,
            IParameterReferenceOperation parameter => !SymbolEqualityComparer.Default.Equals(parameter.Parameter, entityParameter),
            IDefaultValueOperation => true,
            _ => false,
        };
    }

    public static bool IsEntityPropertyPath(IOperation? operation, IParameterSymbol entityParameter) =>
        TryGetEntityProperty(operation, entityParameter, out _);

    public static bool IsSimpleMappedProperty(IPropertySymbol property)
    {
        if (property.ContainingType.SpecialType != SpecialType.None)
        {
            return false;
        }

        foreach (var syntaxReference in property.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is PropertyDeclarationSyntax declaration)
            {
                if (declaration.ExpressionBody is not null)
                {
                    return false;
                }

                var getter = declaration.AccessorList?.Accessors
                    .FirstOrDefault(static accessor => accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration));
                if (getter is not null && (getter.Body is not null || getter.ExpressionBody is not null))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool TryGetLambda(IOperation operation, out IAnonymousFunctionOperation lambda)
    {
        operation = Unwrap(operation);
        if (operation is IDelegateCreationOperation delegateCreation && delegateCreation.Target is not null)
        {
            operation = Unwrap(delegateCreation.Target);
        }

        if (operation is IAnonymousFunctionOperation anonymousFunction)
        {
            lambda = anonymousFunction;
            return true;
        }

        lambda = null!;
        return false;
    }

    public static IOperation? GetLambdaExpression(IAnonymousFunctionOperation lambda)
    {
        if (lambda.Body is IBlockOperation block)
        {
            return block.Operations
                .OfType<IReturnOperation>()
                .SingleOrDefault()
                ?.ReturnedValue;
        }

        return lambda.Body;
    }

    private static bool TryAnalyzeSourceCore(
        IOperation operation,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        EfModelKeys? modelKeys,
        ImmutableArray<string>.Builder operations,
        int localHops,
        out IOperation origin,
        out RowBound rowBound)
    {
        operation = Unwrap(operation);
        if (IsOrDerivesFrom(operation.Type, dbSet))
        {
            origin = operation;
            rowBound = RowBound.None;
            return true;
        }

        // A query local continues the chain when its value at this read is statically determined.
        if (EfQueryLocals.TryFollow(operation, localHops, out var local, out var localValue))
        {
            if (!TryAnalyzeSourceCore(
                localValue,
                queryable,
                dbSet,
                dbContext,
                efExtensions,
                modelKeys,
                operations,
                localHops + 1,
                out origin,
                out rowBound))
            {
                origin = operation;
                rowBound = RowBound.None;
                return false;
            }

            operations.Add($"local '{local.Name}'");
            return true;
        }

        if (operation is not IInvocationOperation invocation)
        {
            origin = operation;
            rowBound = RowBound.None;
            return false;
        }

        var method = NormalizeMethod(invocation.TargetMethod);
        if (method.Name == "Set" && IsOrDerivesFrom(method.ContainingType, dbContext))
        {
            origin = invocation;
            rowBound = RowBound.None;
            return true;
        }

        var containingType = method.ContainingType.OriginalDefinition;
        var isQueryable = SymbolEqualityComparer.Default.Equals(containingType, queryable);
        var isEfComposition = BoundNeutralEfMethods.Contains(method.Name)
            && (SymbolEqualityComparer.Default.Equals(containingType, efExtensions)
                || method.ContainingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore");
        if (!isQueryable && !isEfComposition)
        {
            origin = operation;
            rowBound = RowBound.None;
            return false;
        }

        var source = GetInvocationSource(invocation);
        if (source is null
            || !TryAnalyzeSourceCore(
                source,
                queryable,
                dbSet,
                dbContext,
                efExtensions,
                modelKeys,
                operations,
                localHops,
                out origin,
                out rowBound))
        {
            origin = operation;
            rowBound = RowBound.None;
            return false;
        }

        operations.Add($"{(isQueryable ? "Queryable" : "EF")}.{method.Name}");
        if (isQueryable && method.Name == "Take")
        {
            rowBound = Stronger(rowBound, new RowBound(RowBoundKind.Take, RowBoundStrength.Strong));
        }
        else if (isQueryable && method.Name == "Where")
        {
            rowBound = Stronger(rowBound, ClassifyWhereBound(invocation, modelKeys));
        }

        return true;
    }

    private static RowBound ClassifyWhereBound(IInvocationOperation where, EfModelKeys? modelKeys)
    {
        var predicate = where.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 1)?.Value;
        if (predicate is null || !TryGetLambda(predicate, out var lambda) || lambda.Symbol.Parameters.Length != 1)
        {
            return RowBound.None;
        }

        var entityParameter = lambda.Symbol.Parameters[0];
        var expression = GetLambdaExpression(lambda);
        if (expression is null)
        {
            return RowBound.None;
        }

        return ClassifyPredicateBound(expression, entityParameter, modelKeys);
    }

    private static RowBound ClassifyPredicateBound(IOperation expression, IParameterSymbol entityParameter, EfModelKeys? modelKeys)
    {
        expression = Unwrap(expression);

        // A conjunction can only narrow the result set, so the strongest bound
        // among its conjuncts bounds the whole predicate. Disjunctions are not
        // decomposed: an unbounded branch of an `||` keeps the result unbounded.
        if (expression is IBinaryOperation conjunction
            && conjunction.OperatorMethod is null
            && conjunction.OperatorKind == BinaryOperatorKind.ConditionalAnd)
        {
            return Stronger(
                ClassifyPredicateBound(conjunction.LeftOperand, entityParameter, modelKeys),
                ClassifyPredicateBound(conjunction.RightOperand, entityParameter, modelKeys));
        }

        if (IsLocalCollectionMembership(expression, entityParameter))
        {
            return new RowBound(RowBoundKind.LocalCollectionMembership, RowBoundStrength.Strong);
        }

        if (IsEqualsInvocation(expression, out var receiver, out var argument)
            && TryGetComparedEntityProperty(receiver, argument, entityParameter, out var property, out var entityType))
        {
            return ClassifyKeyEquality(property, entityType, modelKeys);
        }

        if (expression is not IBinaryOperation binary)
        {
            return RowBound.None;
        }

        if (binary.OperatorKind == BinaryOperatorKind.Equals
            && TryGetComparedEntityProperty(binary.LeftOperand, binary.RightOperand, entityParameter, out property, out entityType)
            && IsValueEqualityOperator(binary.OperatorMethod, property.Type))
        {
            return ClassifyKeyEquality(property, entityType, modelKeys);
        }

        if (binary.OperatorKind is BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual
            or BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual
            && TryGetComparedEntityProperty(binary.LeftOperand, binary.RightOperand, entityParameter, out property, out _)
            && IsTemporal(property.Type))
        {
            return new RowBound(RowBoundKind.TimeWindow, RowBoundStrength.Weak);
        }

        return RowBound.None;
    }

    private static RowBound ClassifyKeyEquality(IPropertySymbol property, ITypeSymbol entityType, EfModelKeys? modelKeys) =>
        HasKeyMetadata(property, entityType) || modelKeys?.IsKey(entityType, property.Name) == true
            ? new RowBound(RowBoundKind.KeyEquality, RowBoundStrength.Strong)
            : IsKeyName(property)
                ? new RowBound(RowBoundKind.KeyEqualityByName, RowBoundStrength.Medium)
                : RowBound.None;

    // A built-in `==`, or a user-defined `==` over the compared type such as `Guid`'s
    // (lifted for `Guid?`), is value equality that EF Core translates to SQL `=`.
    private static bool IsValueEqualityOperator(IMethodSymbol? operatorMethod, ITypeSymbol comparedType)
    {
        if (operatorMethod is null)
        {
            return true;
        }

        var type = UnwrapNullable(comparedType);
        return operatorMethod.Name == WellKnownMemberNames.EqualityOperatorName
            && operatorMethod.Parameters.Length == 2
            && operatorMethod.Parameters.All(parameter =>
                SymbolEqualityComparer.Default.Equals(UnwrapNullable(parameter.Type), type));
    }

    // `a.Equals(b)` through `IEquatable<T>.Equals(T)` or `object.Equals(object)`.
    private static bool IsEqualsInvocation(IOperation operation, out IOperation receiver, out IOperation argument)
    {
        receiver = null!;
        argument = null!;
        if (operation is not IInvocationOperation invocation
            || invocation.Instance is null
            || invocation.TargetMethod.IsStatic
            || invocation.TargetMethod.Name != nameof(Equals)
            || invocation.TargetMethod.ReturnType.SpecialType != SpecialType.System_Boolean
            || invocation.Arguments.Length != 1)
        {
            return false;
        }

        receiver = invocation.Instance;
        argument = invocation.Arguments[0].Value;
        return true;
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    private static bool IsLocalCollectionMembership(IOperation operation, IParameterSymbol entityParameter)
    {
        if (operation is not IInvocationOperation invocation)
        {
            return false;
        }

        var method = NormalizeMethod(invocation.TargetMethod);
        if (method.Name == "Contains")
        {
            var source = GetInvocationSource(invocation);
            var value = invocation.Instance is not null
                ? invocation.Arguments.OrderBy(static argument => argument.Parameter?.Ordinal).Select(static argument => argument.Value).FirstOrDefault()
                : invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 1)?.Value;
            return source is not null
                && value is not null
                && IsCollectionReference(source)
                && IsEntityPropertyPath(value, entityParameter);
        }

        if (method.Name != "Any" || method.ContainingNamespace.ToDisplayString() != "System.Linq")
        {
            return false;
        }

        var anySource = GetInvocationSource(invocation);
        var predicate = invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 1)?.Value;
        if (anySource is null
            || predicate is null
            || !IsCollectionReference(anySource)
            || !TryGetLambda(predicate, out var lambda)
            || lambda.Symbol.Parameters.Length != 1)
        {
            return false;
        }

        var expression = GetLambdaExpression(lambda);
        if (expression is not IBinaryOperation equality
            || equality.OperatorKind != BinaryOperatorKind.Equals
            || (equality.OperatorMethod is not null
                && !IsValueEqualityOperator(equality.OperatorMethod, lambda.Symbol.Parameters[0].Type)))
        {
            return false;
        }

        var collectionParameter = lambda.Symbol.Parameters[0];
        return (IsParameterReference(equality.LeftOperand, collectionParameter)
                && IsEntityPropertyPath(equality.RightOperand, entityParameter))
            || (IsParameterReference(equality.RightOperand, collectionParameter)
                && IsEntityPropertyPath(equality.LeftOperand, entityParameter));
    }

    private static bool IsCollectionReference(IOperation operation)
    {
        operation = Unwrap(operation);
        return operation is ILocalReferenceOperation
                or IParameterReferenceOperation
                or IFieldReferenceOperation
            && IsCollectionType(operation.Type);
    }

    private static bool IsCollectionType(ITypeSymbol? type)
    {
        if (type is null || type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        return type is IArrayTypeSymbol
            || type.AllInterfaces.Any(static @interface =>
                @interface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>"
                || @interface.ToDisplayString() == "System.Collections.IEnumerable");
    }

    private static bool IsParameterReference(IOperation operation, IParameterSymbol parameter)
    {
        operation = Unwrap(operation);
        return operation is IParameterReferenceOperation reference
            && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);
    }

    private static bool TryGetComparedEntityProperty(
        IOperation left,
        IOperation right,
        IParameterSymbol entityParameter,
        out IPropertySymbol property,
        out ITypeSymbol entityType)
    {
        if (TryGetEntityProperty(left, entityParameter, out property, out entityType)
            && IsBoundScalar(right, entityParameter))
        {
            return true;
        }

        return TryGetEntityProperty(right, entityParameter, out property, out entityType)
            && IsBoundScalar(left, entityParameter);
    }

    private static bool IsBoundScalar(IOperation operation, IParameterSymbol entityParameter)
    {
        operation = Unwrap(operation);

        // The comparand must be a single scalar value external to the Where entity.
        // A property path over the entity parameter is the entity side of the
        // comparison, not a comparand, so `x.A == x.B` (both entity properties)
        // establishes no single-row bound and is rejected here.
        if (IsEntityPropertyPath(operation, entityParameter))
        {
            return false;
        }

        // A constant, local, non-entity parameter, default expression, field, or a
        // property/member access rooted on another object (for example `parent.Id` or
        // `product.ProductId`) is each a single scalar value that bounds an entity-key
        // equality to at most one row.
        return operation.ConstantValue.HasValue
            || operation is ILocalReferenceOperation
                or IFieldReferenceOperation
                or IPropertyReferenceOperation
                or IDefaultValueOperation
            || (operation is IParameterReferenceOperation parameter
                && !SymbolEqualityComparer.Default.Equals(parameter.Parameter, entityParameter));
    }

    private static bool TryGetEntityProperty(
        IOperation? operation,
        IParameterSymbol entityParameter,
        out IPropertySymbol property) =>
        TryGetEntityProperty(operation, entityParameter, out property, out _);

    // `entityType` is the type the outermost property is read from: the Where entity for
    // `x.Key`, or the navigation's type for `x.Parent.Key`.
    private static bool TryGetEntityProperty(
        IOperation? operation,
        IParameterSymbol entityParameter,
        out IPropertySymbol property,
        out ITypeSymbol entityType)
    {
        property = null!;
        entityType = null!;
        if (operation is null)
        {
            return false;
        }

        operation = Unwrap(operation);
        operation = UnwrapNullableAccess(operation);
        IPropertySymbol? outermostProperty = null;
        ITypeSymbol? outermostReceiver = null;
        while (operation is IPropertyReferenceOperation reference && reference.Instance is not null)
        {
            if (reference.Property.IsStatic
                || reference.Property.GetMethod is null
                || !IsSimpleMappedProperty(reference.Property))
            {
                return false;
            }

            if (outermostProperty is null)
            {
                outermostProperty = reference.Property;
                outermostReceiver = reference.Instance.Type;
            }

            operation = UnwrapNullableAccess(Unwrap(reference.Instance));
        }

        if (outermostProperty is null
            || operation is not IParameterReferenceOperation parameter
            || !SymbolEqualityComparer.Default.Equals(parameter.Parameter, entityParameter))
        {
            return false;
        }

        property = outermostProperty;
        entityType = outermostReceiver ?? outermostProperty.ContainingType;
        return true;
    }

    private static bool HasKeyMetadata(IPropertySymbol property, ITypeSymbol entityType)
    {
        if (IsNavigationForeignKey(property, entityType))
        {
            return true;
        }

        if (property.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString() is
                "System.ComponentModel.DataAnnotations.KeyAttribute"
                or "System.ComponentModel.DataAnnotations.Schema.ForeignKeyAttribute"))
        {
            return true;
        }

        // Only a unique, single-column index is an alternate key that bounds a
        // single-property equality to one row. A non-unique index, or one member of
        // a composite index, does not bound cardinality, so it is not a strong bound.
        return property.ContainingType.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "Microsoft.EntityFrameworkCore.IndexAttribute"
            && IsUniqueSingleColumnIndexFor(attribute, property.Name));
    }

    private static bool IsUniqueSingleColumnIndexFor(AttributeData attribute, string propertyName)
    {
        var isUnique = attribute.NamedArguments.Any(static argument =>
            string.Equals(argument.Key, "IsUnique", StringComparison.Ordinal)
            && argument.Value.Value is true);
        if (!isUnique)
        {
            return false;
        }

        var columns = attribute.ConstructorArguments
            .SelectMany(static argument => argument.Kind == TypedConstantKind.Array
                ? argument.Values.Select(static value => value.Value as string)
                : new[] { argument.Value as string })
            .Where(static name => name is not null)
            .ToArray();

        return columns.Length == 1 && string.Equals(columns[0], propertyName, StringComparison.Ordinal);
    }

    // EF Core's convention: `<Navigation>Id` is the foreign key of a reference navigation
    // named `<Navigation>` on the same entity type or one of its base types.
    private static bool IsNavigationForeignKey(IPropertySymbol property, ITypeSymbol entityType)
    {
        var name = property.Name;
        if (name.Length <= 2 || !name.EndsWith("Id", StringComparison.Ordinal))
        {
            return false;
        }

        var navigationName = name.Substring(0, name.Length - 2);
        for (var type = entityType; type is not null; type = type.BaseType)
        {
            foreach (var navigation in type.GetMembers(navigationName).OfType<IPropertySymbol>())
            {
                if (IsReferenceNavigationType(navigation.Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsReferenceNavigationType(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Class
        && type.SpecialType != SpecialType.System_String
        && type.SpecialType != SpecialType.System_Object
        && type is not IArrayTypeSymbol
        && !type.AllInterfaces.Any(static @interface => @interface.SpecialType == SpecialType.System_Collections_IEnumerable
            || @interface.ToDisplayString() == "System.Collections.IEnumerable");

    private static bool IsKeyName(IPropertySymbol property)
    {
        return string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)
            || string.Equals(property.Name, property.ContainingType.Name + "Id", StringComparison.OrdinalIgnoreCase)
            || property.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTemporal(ITypeSymbol type)
    {
        var named = type as INamedTypeSymbol;
        if (named?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            type = named.TypeArguments[0];
        }

        return type.ToDisplayString() is "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly";
    }

    private static RowBound Stronger(RowBound current, RowBound candidate) =>
        candidate.Strength > current.Strength ? candidate : current;
}
