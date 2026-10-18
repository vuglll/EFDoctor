using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueryOutlivesContextAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD030";
    public const string RuleTitle = "Query returned after its DbContext is disposed";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD030";
    public const string Impact = "An EF Core query runs when it is enumerated. This one is returned to the caller after its DbContext is disposed, so enumerating it throws an ObjectDisposedException (\"Cannot access a disposed context instance\").";

    private const string QueryableInterfaceMetadataName = "System.Linq.IQueryable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "This query runs after its DbContext '{0}' is disposed";
    private const string Remediation = "Materialize the result before returning, for example with ToList() or await ToListAsync(), or let the caller own the context's lifetime, for example by injecting a scoped DbContext instead of creating and disposing one in this method.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Reliability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports a deferred EF Core query returned from inside the using scope that disposes its DbContext, or the service scope the DbContext was resolved from.",
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

        var queryableInterface = context.Compilation.GetTypeByMetadataName(QueryableInterfaceMetadataName);
        var queryable = context.Compilation.GetTypeByMetadataName(QueryableMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (queryableInterface is null || queryable is null || dbSet is null || dbContext is null || efExtensions is null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeReturn(operationContext, queryableInterface, queryable, dbSet, dbContext, efExtensions),
            OperationKind.Return);
    }

    private static void AnalyzeReturn(
        OperationAnalysisContext context,
        INamedTypeSymbol queryableInterface,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions)
    {
        var returnOperation = (IReturnOperation)context.Operation;
        if (returnOperation.ReturnedValue is not { } returned
            || !TryGetDeferredQuery(EfQueryOperationAnalysis.Unwrap(returned), queryableInterface, out var query)
            || !EfQueryOperationAnalysis.TryAnalyzeSource(query, queryable, dbSet, dbContext, efExtensions, out var analysis, followComposedLocals: true))
        {
            return;
        }

        var root = Root(returnOperation);
        var identity = new EfContextIdentity(root, dbContext);
        if (EfQueryOperationAnalysis.Unwrap(identity.ContextOfDbSet(analysis.Origin) ?? analysis.Origin) is not ILocalReferenceOperation contextLocal
            || !identity.TryGetKey(contextLocal, out _, out _)
            || !TryDescribeDisposal(contextLocal.Local, returnOperation, root, identity, out var disposal))
        {
            return;
        }

        var evidence = $"The returned query is traced to DbSet origin '{analysis.Origin.Syntax}' on context '{contextLocal.Local.Name}', which {disposal}. The query runs only when the caller enumerates it, after the context is disposed.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, returned.Syntax.GetLocation(), properties, contextLocal.Local.Name));
    }

    // A value that implements IQueryable, or AsEnumerable/AsAsyncEnumerable over one: either way
    // nothing runs until the caller enumerates it.
    private static bool TryGetDeferredQuery(IOperation value, INamedTypeSymbol queryableInterface, out IOperation query)
    {
        query = value;
        if (ImplementsQueryable(value.Type, queryableInterface))
        {
            return true;
        }

        if (value is IInvocationOperation invocation
            && EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod).Name is "AsEnumerable" or "AsAsyncEnumerable"
            && EfQueryOperationAnalysis.GetInvocationSource(invocation) is { } source
            && ImplementsQueryable(EfQueryOperationAnalysis.Unwrap(source).Type, queryableInterface))
        {
            query = EfQueryOperationAnalysis.Unwrap(source);
            return true;
        }

        return false;
    }

    private static bool ImplementsQueryable(ITypeSymbol? type, INamedTypeSymbol queryableInterface) =>
        type is not null
        && (SymbolEqualityComparer.Default.Equals(type, queryableInterface)
            || type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, queryableInterface)));

    // The context local is disposed at the return when it, or the service scope it was resolved
    // from, is a using local whose scope contains the return in the same function.
    private static bool TryDescribeDisposal(ILocalSymbol local, IReturnOperation returnOperation, IOperation root, EfContextIdentity identity, out string disposal)
    {
        if (IsUsingLocalInScope(local, returnOperation, root, out var form))
        {
            disposal = $"is declared by a {form} that is disposed when the method returns";
            return true;
        }

        if (TryGetServiceScope(local, root, identity, out var scope)
            && IsUsingLocalInScope(scope, returnOperation, root, out form))
        {
            disposal = $"is resolved from service scope '{scope.Name}', declared by a {form} that is disposed, with its scoped services, when the method returns";
            return true;
        }

        disposal = null!;
        return false;
    }

    private static bool IsUsingLocalInScope(ILocalSymbol local, IReturnOperation returnOperation, IOperation root, out string form)
    {
        form = null!;
        var declarator = root.Descendants()
            .OfType<IVariableDeclaratorOperation>()
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, local));
        if (declarator is null)
        {
            return false;
        }

        var function = EnclosingFunction(returnOperation);
        for (var current = declarator.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IUsingDeclarationOperation usingDeclaration
                    when usingDeclaration.Parent is IBlockOperation block
                        && IsAfterWithin(returnOperation, block, usingDeclaration)
                        && ReferenceEquals(EnclosingFunction(usingDeclaration), function):
                    form = usingDeclaration.IsAsynchronous ? "'await using' declaration" : "'using' declaration";
                    return true;
                case IUsingOperation usingStatement
                    when IsDescendantOf(returnOperation, usingStatement.Body)
                        && ReferenceEquals(EnclosingFunction(usingStatement), function):
                    form = usingStatement.IsAsynchronous ? "'await using' statement" : "'using' statement";
                    return true;
                case IUsingDeclarationOperation or IUsingOperation:
                    return false;
                case IBlockOperation or IAnonymousFunctionOperation or ILocalFunctionOperation:
                    return false;
            }
        }

        return false;
    }

    // `var db = scope.ServiceProvider.GetRequiredService<TContext>()`, `GetService<TContext>()`, or a
    // cast `GetService(typeof(TContext))`, in instance or extension form, where `scope` is a local.
    private static bool TryGetServiceScope(ILocalSymbol local, IOperation root, EfContextIdentity identity, out ILocalSymbol scope)
    {
        scope = null!;
        var declarator = root.Descendants()
            .OfType<IVariableDeclaratorOperation>()
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, local));
        if (declarator?.Initializer?.Value is not { } initializer
            || UnwrapCast(initializer) is not IInvocationOperation resolve
            || EfQueryOperationAnalysis.NormalizeMethod(resolve.TargetMethod).Name is not ("GetRequiredService" or "GetService")
            || EfQueryOperationAnalysis.GetInvocationSource(resolve) is not { } provider
            || EfQueryOperationAnalysis.Unwrap(provider) is not IPropertyReferenceOperation { Property.Name: "ServiceProvider", Instance: { } scopeReference }
            || EfQueryOperationAnalysis.Unwrap(scopeReference) is not ILocalReferenceOperation scopeLocal
            || !identity.TryGetKey(scopeLocal, out _, out _))
        {
            return false;
        }

        scope = scopeLocal.Local;
        return true;
    }

    // `(TContext)provider.GetService(typeof(TContext))` casts the resolved service explicitly.
    private static IOperation UnwrapCast(IOperation operation)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        while (operation is IConversionOperation { IsImplicit: false } conversion)
        {
            operation = EfQueryOperationAnalysis.Unwrap(conversion.Operand);
        }

        return operation;
    }

    private static bool IsAfterWithin(IOperation operation, IBlockOperation block, IOperation statement)
    {
        var statementIndex = block.Operations.IndexOf(statement);
        for (var current = operation; current.Parent is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.Parent, block))
            {
                return block.Operations.IndexOf(current) > statementIndex;
            }
        }

        return false;
    }

    private static bool IsDescendantOf(IOperation operation, IOperation ancestor)
    {
        for (var current = operation; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static IOperation? EnclosingFunction(IOperation operation)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return current;
            }
        }

        return null;
    }

    private static IOperation Root(IOperation operation)
    {
        while (operation.Parent is not null)
        {
            operation = operation.Parent;
        }

        return operation;
    }
}
