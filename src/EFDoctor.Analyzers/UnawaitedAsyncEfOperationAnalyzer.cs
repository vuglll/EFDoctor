using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnawaitedAsyncEfOperationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD018";
    public const string RuleTitle = "Unawaited EF Core async operation";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD018";
    public const string Impact = "A discarded EF Core task runs as fire-and-forget: the operation may not complete before the DbContext is reused or disposed, its exceptions are never observed, and a later operation on the same context can overlap it and fail with a concurrent-use error or lose data; the actual outcome depends on timing.";

    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string RelationalDatabaseExtensionsMetadataName = "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions";
    private const string Message = "The task returned by this EF Core asynchronous operation is discarded instead of awaited";
    private const string Remediation = "Await the EF Core operation and propagate async through the caller. If the work must run in the background, run it on a separately scoped context (for example from IDbContextFactory<TContext> or a new dependency-injection scope) and observe the task so failures are logged, rather than discarding it.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Correctness",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports resolved EF Core asynchronous operations whose returned task is discarded by an expression statement, a discard assignment, or a void-returning lambda.",
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

        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (dbContext is null || dbSet is null || efExtensions is null)
        {
            return;
        }

        var relationalDatabaseExtensions = context.Compilation.GetTypeByMetadataName(RelationalDatabaseExtensionsMetadataName);
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, dbContext, dbSet, efExtensions, relationalDatabaseExtensions),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol? relationalDatabaseExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        if (!EfAsyncOperations.IsCoreOperation(method, dbContext, dbSet, efExtensions)
            && !IsExtendedOperation(method, dbContext, dbSet, relationalDatabaseExtensions))
        {
            return;
        }

        if (!TryGetDiscard(invocation, out var discarded, out var form, out var configured))
        {
            return;
        }

        var operationName = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var evidence = $"The task returned by resolved EF Core operation '{operationName}' is discarded {form}; nothing awaits or observes it.";
        if (configured)
        {
            evidence += " ConfigureAwait only configures a later await and does not await the task.";
        }

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, discarded.Syntax.GetLocation(), properties));
    }

    private static bool IsExtendedOperation(
        IMethodSymbol method,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol? relationalDatabaseExtensions)
    {
        if (method.Name is "AddAsync" or "AddRangeAsync")
        {
            return EfAsyncOperations.IsDbContextMethod(method, dbContext)
                || EfQueryOperationAnalysis.IsOrDerivesFrom(method.ContainingType, dbSet);
        }

        return relationalDatabaseExtensions is not null
            && method.Name.StartsWith("ExecuteSql", StringComparison.Ordinal)
            && method.Name.EndsWith("Async", StringComparison.Ordinal)
            && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, relationalDatabaseExtensions);
    }

    // Walks outward from the EF operation through wrappers that do not observe the task. The
    // first real consumer decides the outcome: only an expression statement or a discard
    // assignment discards the task; any other consumer awaits, stores, passes, or blocks on it.
    private static bool TryGetDiscard(
        IInvocationOperation efOperation,
        out IOperation discarded,
        out string form,
        out bool configured)
    {
        IOperation current = efOperation;
        configured = false;
        while (true)
        {
            var parent = current.Parent;
            switch (parent)
            {
                case IParenthesizedOperation:
                case IConversionOperation { IsImplicit: true }:
                    current = parent;
                    continue;
                case IConditionalAccessOperation conditionalAccess
                    when ReferenceEquals(conditionalAccess.WhenNotNull, current):
                    current = parent;
                    continue;
                case IInvocationOperation configureAwait
                    when ReferenceEquals(configureAwait.Instance, current) && IsConfigureAwait(configureAwait):
                    configured = true;
                    current = parent;
                    continue;
                case IExpressionStatementOperation statement:
                    discarded = current;
                    form = IsVoidLambdaBody(statement)
                        ? "as the body of a void-returning lambda"
                        : "as a standalone statement";
                    return true;
                case ISimpleAssignmentOperation assignment
                    when ReferenceEquals(assignment.Value, current) && assignment.Target is IDiscardOperation:
                    discarded = assignment;
                    form = "through an explicit discard assignment";
                    return true;
                default:
                    discarded = current;
                    form = string.Empty;
                    return false;
            }
        }
    }

    private static bool IsConfigureAwait(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name != "ConfigureAwait" || invocation.TargetMethod.IsStatic)
        {
            return false;
        }

        var containingType = invocation.TargetMethod.ContainingType.OriginalDefinition.ToDisplayString();
        return containingType is "System.Threading.Tasks.Task"
            or "System.Threading.Tasks.Task<TResult>"
            or "System.Threading.Tasks.ValueTask"
            or "System.Threading.Tasks.ValueTask<TResult>";
    }

    private static bool IsVoidLambdaBody(IExpressionStatementOperation statement) =>
        statement.IsImplicit
        && statement.Parent is IBlockOperation { IsImplicit: true } block
        && block.Parent is IAnonymousFunctionOperation;
}
