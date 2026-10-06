using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyncOverAsyncEfOperationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD011";
    public const string RuleTitle = "Synchronous blocking on an EF Core async operation";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD011";
    public const string Impact = "Synchronously waiting for database I/O occupies the calling thread, reduces scalability, and can contribute to thread-pool starvation or a context-dependent deadlock; this diagnostic does not establish that a runtime failure has occurred.";

    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string TaskMetadataName = "System.Threading.Tasks.Task";
    private const string GenericTaskMetadataName = "System.Threading.Tasks.Task`1";
    private const string GenericValueTaskMetadataName = "System.Threading.Tasks.ValueTask`1";
    private const string Message = "This code synchronously blocks on an EF Core asynchronous operation";
    private const string Remediation = "Await the EF Core operation and propagate async through the caller. If a genuinely synchronous integration boundary cannot be changed, isolate and document that boundary rather than adding another blocking wrapper.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports direct synchronous blocking consumption of supported EF Core asynchronous operations.",
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
        var task = context.Compilation.GetTypeByMetadataName(TaskMetadataName);
        var genericTask = context.Compilation.GetTypeByMetadataName(GenericTaskMetadataName);
        var genericValueTask = context.Compilation.GetTypeByMetadataName(GenericValueTaskMetadataName);
        if (dbContext is null
            || dbSet is null
            || efExtensions is null
            || task is null
            || genericTask is null
            || genericValueTask is null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeProperty(
                operationContext,
                dbContext,
                dbSet,
                efExtensions,
                genericTask,
                genericValueTask),
            OperationKind.PropertyReference);
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(
                operationContext,
                dbContext,
                dbSet,
                efExtensions,
                task),
            OperationKind.Invocation);
    }

    private static void AnalyzeProperty(
        OperationAnalysisContext context,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol genericTask,
        INamedTypeSymbol genericValueTask)
    {
        var property = (IPropertyReferenceOperation)context.Operation;
        if (property.Property.Name != "Result"
            || property.Instance is null
            || (!IsOriginalDefinition(property.Property.ContainingType, genericTask)
                && !IsOriginalDefinition(property.Property.ContainingType, genericValueTask))
            || !TryGetEfOperation(property.Instance, dbContext, dbSet, efExtensions, out var efOperation))
        {
            return;
        }

        Report(context, property, efOperation, ".Result");
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol task)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (IsParameterlessTaskWait(invocation, task)
            && invocation.Instance is not null
            && TryGetEfOperation(invocation.Instance, dbContext, dbSet, efExtensions, out var waitedOperation))
        {
            Report(context, invocation, waitedOperation, ".Wait()");
            return;
        }

        if (IsFrameworkGetResult(invocation)
            && TryGetAwaitedSource(invocation, out var source)
            && TryGetEfOperation(source, dbContext, dbSet, efExtensions, out var awaitedOperation))
        {
            Report(context, invocation, awaitedOperation, ".GetAwaiter().GetResult()");
        }
    }

    private static bool IsParameterlessTaskWait(IInvocationOperation invocation, INamedTypeSymbol task) =>
        invocation.TargetMethod.Name == "Wait"
        && !invocation.TargetMethod.IsStatic
        && invocation.TargetMethod.Parameters.Length == 0
        && invocation.Arguments.Length == 0
        && EfQueryOperationAnalysis.IsOrDerivesFrom(invocation.TargetMethod.ContainingType, task);

    private static bool IsFrameworkGetResult(IInvocationOperation invocation)
    {
        var containingType = invocation.TargetMethod.ContainingType;
        return invocation.TargetMethod.Name == "GetResult"
            && !invocation.TargetMethod.IsStatic
            && invocation.TargetMethod.Parameters.Length == 0
            && containingType.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices"
            && containingType.Name.EndsWith("Awaiter", StringComparison.Ordinal);
    }

    private static bool TryGetAwaitedSource(IInvocationOperation getResult, out IOperation source)
    {
        var getAwaiter = EfQueryOperationAnalysis.Unwrap(getResult.Instance!);
        if (getAwaiter is not IInvocationOperation getAwaiterInvocation
            || getAwaiterInvocation.TargetMethod.Name != "GetAwaiter"
            || getAwaiterInvocation.TargetMethod.Parameters.Length != 0
            || getAwaiterInvocation.Instance is null)
        {
            source = getResult;
            return false;
        }

        source = EfQueryOperationAnalysis.Unwrap(getAwaiterInvocation.Instance);
        if (source is IInvocationOperation configureAwait
            && configureAwait.TargetMethod.Name == "ConfigureAwait"
            && configureAwait.TargetMethod.Parameters.Length == 1
            && configureAwait.TargetMethod.Parameters[0].Type.SpecialType == SpecialType.System_Boolean
            && configureAwait.Instance is not null
            && IsTaskLike(configureAwait.TargetMethod.ContainingType))
        {
            source = configureAwait.Instance;
        }

        return true;
    }

    private static bool TryGetEfOperation(
        IOperation operation,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol efExtensions,
        out IInvocationOperation efOperation) =>
        EfAsyncOperations.TryGetCoreOperation(operation, dbContext, dbSet, efExtensions, out efOperation);

    private static bool IsOriginalDefinition(ITypeSymbol? actual, INamedTypeSymbol expected) =>
        actual is INamedTypeSymbol named
        && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, expected);

    private static bool IsTaskLike(ITypeSymbol type)
    {
        var displayName = type.OriginalDefinition.ToDisplayString();
        return displayName == "System.Threading.Tasks.Task"
            || displayName == "System.Threading.Tasks.Task<TResult>"
            || displayName == "System.Threading.Tasks.ValueTask<TResult>";
    }

    private static void Report(
        OperationAnalysisContext context,
        IOperation blockingOperation,
        IInvocationOperation efOperation,
        string blockingApi)
    {
        var method = EfQueryOperationAnalysis.NormalizeMethod(efOperation.TargetMethod);
        var operationName = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var evidence = $"The blocking API '{blockingApi}' directly consumes resolved EF Core operation '{operationName}'.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, blockingOperation.Syntax.GetLocation(), properties));
    }
}
