using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

internal static class EfAsyncOperations
{
    // Recognizes the core resolved EF Core asynchronous operations: asynchronous queryable
    // extensions (query terminals and bulk operations), DbContext.SaveChangesAsync including
    // overrides, and DbSet<T>.FindAsync.
    public static bool TryGetCoreOperation(
        IOperation operation,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol efExtensions,
        out IInvocationOperation efOperation)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        if (operation is IInvocationOperation invocation
            && IsCoreOperation(EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod), dbContext, dbSet, efExtensions))
        {
            efOperation = invocation;
            return true;
        }

        efOperation = null!;
        return false;
    }

    public static bool IsCoreOperation(
        IMethodSymbol method,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol efExtensions) =>
        (method.Name.EndsWith("Async", StringComparison.Ordinal)
            && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, efExtensions))
        || (method.Name == "SaveChangesAsync" && IsDbContextMethod(method, dbContext))
        || (method.Name == "FindAsync" && EfQueryOperationAnalysis.IsOrDerivesFrom(method.ContainingType, dbSet));

    public static bool IsDbContextMethod(IMethodSymbol method, INamedTypeSymbol dbContext)
    {
        for (var current = method; current is not null; current = current.OverriddenMethod)
        {
            if (EfQueryOperationAnalysis.IsOrDerivesFrom(current.ContainingType, dbContext))
            {
                return true;
            }
        }

        return false;
    }
}
