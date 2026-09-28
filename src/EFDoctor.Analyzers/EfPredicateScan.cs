using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

// Recognizes predicate lambdas that EF Core translates to SQL: the predicate of a
// Queryable or EF Core async operator whose inline source is a proven DbSet query and
// whose parameter is that query's entity type.
internal sealed class EfPredicateScan
{
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";

    private static readonly ImmutableHashSet<string> QueryablePredicateMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Where",
            "Any",
            "All",
            "Count",
            "LongCount",
            "First",
            "FirstOrDefault",
            "Single",
            "SingleOrDefault",
            "Last",
            "LastOrDefault");

    private static readonly ImmutableHashSet<string> EfAsyncPredicateMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "AnyAsync",
            "AllAsync",
            "CountAsync",
            "LongCountAsync",
            "FirstAsync",
            "FirstOrDefaultAsync",
            "SingleAsync",
            "SingleOrDefaultAsync",
            "LastAsync",
            "LastOrDefaultAsync");

    private readonly INamedTypeSymbol _queryable;
    private readonly INamedTypeSymbol _dbSet;
    private readonly INamedTypeSymbol _dbContext;
    private readonly INamedTypeSymbol _efExtensions;

    private EfPredicateScan(
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions)
    {
        _queryable = queryable;
        _dbSet = dbSet;
        _dbContext = dbContext;
        _efExtensions = efExtensions;
    }

    public static EfPredicateScan? Create(Compilation compilation)
    {
        var queryable = compilation.GetTypeByMetadataName(QueryableMetadataName);
        var dbSet = compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        return queryable is null || dbSet is null || dbContext is null || efExtensions is null
            ? null
            : new EfPredicateScan(queryable, dbSet, dbContext, efExtensions);
    }

    public bool TryGetPredicate(IInvocationOperation invocation, out SqlPredicate predicate)
    {
        predicate = null!;
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        var containingType = method.ContainingType.OriginalDefinition;
        var isPredicateOperator =
            (SymbolEqualityComparer.Default.Equals(containingType, _queryable) && QueryablePredicateMethods.Contains(method.Name))
            || (SymbolEqualityComparer.Default.Equals(containingType, _efExtensions) && EfAsyncPredicateMethods.Contains(method.Name));
        if (!isPredicateOperator)
        {
            return false;
        }

        var predicateArgument = invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 1)?.Value;
        var source = EfQueryOperationAnalysis.GetInvocationSource(invocation);
        if (predicateArgument is null
            || source is null
            || !EfQueryOperationAnalysis.TryGetLambda(predicateArgument, out var lambda)
            || lambda.Symbol.Parameters.Length != 1
            || !EfQueryOperationAnalysis.TryAnalyzeSource(source, _queryable, _dbSet, _dbContext, _efExtensions, out var analysis))
        {
            return false;
        }

        // A predicate after an element-type-changing projection may read computed
        // members rather than columns, so only the origin entity type is analyzed.
        var parameter = lambda.Symbol.Parameters[0];
        if (!SymbolEqualityComparer.Default.Equals(parameter.Type, GetEntityType(analysis.Origin.Type)))
        {
            return false;
        }

        predicate = new SqlPredicate(analysis.Origin, $"{method.ContainingType.Name}.{method.Name}", lambda, parameter);
        return true;
    }

    public static bool TryGetColumnPath(IOperation? operation, IParameterSymbol parameter, out string path)
    {
        path = string.Empty;
        if (operation is null)
        {
            return false;
        }

        var segments = new List<string>();
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        if (operation.Type?.SpecialType != SpecialType.System_String)
        {
            return false;
        }

        while (operation is IPropertyReferenceOperation reference && reference.Instance is not null)
        {
            if (reference.Property.IsStatic
                || reference.Property.GetMethod is null
                || !EfQueryOperationAnalysis.IsSimpleMappedProperty(reference.Property))
            {
                return false;
            }

            segments.Insert(0, reference.Property.Name);
            operation = EfQueryOperationAnalysis.Unwrap(reference.Instance);
        }

        if (segments.Count == 0
            || operation is not IParameterReferenceOperation root
            || !SymbolEqualityComparer.Default.Equals(root.Parameter, parameter))
        {
            return false;
        }

        path = $"{parameter.Type.Name}.{string.Join(".", segments)}";
        return true;
    }

    public static bool ReferencesParameter(IOperation operation, IParameterSymbol parameter) =>
        operation.DescendantsAndSelf().Any(descendant =>
            descendant is IParameterReferenceOperation reference
            && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter));

    private ITypeSymbol? GetEntityType(ITypeSymbol? originType)
    {
        for (var current = originType as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, _dbSet))
            {
                return current.TypeArguments[0];
            }
        }

        return null;
    }
}

internal sealed class SqlPredicate
{
    public SqlPredicate(IOperation origin, string operatorName, IAnonymousFunctionOperation lambda, IParameterSymbol parameter)
    {
        Origin = origin;
        OperatorName = operatorName;
        Lambda = lambda;
        Parameter = parameter;
    }

    public IOperation Origin { get; }

    public string OperatorName { get; }

    public IAnonymousFunctionOperation Lambda { get; }

    public IParameterSymbol Parameter { get; }

    // Nested lambdas have their own parameter and translate as subqueries, which the
    // predicate rules do not analyze.
    public IEnumerable<IOperation> Operations(CancellationToken cancellationToken)
    {
        var pending = new Stack<IOperation>();
        pending.Push(Lambda.Body);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = pending.Pop();
            yield return operation;

            foreach (var child in operation.ChildOperations.Reverse())
            {
                if (child is not IAnonymousFunctionOperation)
                {
                    pending.Push(child);
                }
            }
        }
    }
}
