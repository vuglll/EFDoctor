using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyncDatabaseCallInAsyncAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD010";
    public const string RuleTitle = "Synchronous database call in an async method";
    public const string Confidence = "medium";
    public const string DocumentationKey = "EFD010";
    public const string Impact = "A synchronous EF Core database call blocks the calling thread for the full round trip instead of yielding it, which ties up thread-pool threads under load and reduces the scalability an async method is meant to provide.";

    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";

    private static readonly ImmutableHashSet<string> SyncTerminals = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ToList", "ToArray", "ToDictionary",
        "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
        "Count", "LongCount", "Any", "All",
        "Min", "Max", "Sum", "Average",
        "Contains", "ElementAt", "ElementAtOrDefault");

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        "This synchronous EF Core database call runs inside an async method; await {0} instead",
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports a synchronous EF Core database call inside an async context that has a direct EF async counterpart, unless the other arm of an enclosing conditional already awaits that counterpart.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        var queryable = context.Compilation.GetTypeByMetadataName(QueryableMetadataName);
        var enumerable = context.Compilation.GetTypeByMetadataName(EnumerableMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (queryable is null || enumerable is null || dbSet is null || dbContext is null || efExtensions is null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, queryable, enumerable, dbSet, dbContext, efExtensions),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol queryable,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        var containingType = method.ContainingType.OriginalDefinition;

        string? suggestion = null;

        if (SyncTerminals.Contains(method.Name)
            && (SymbolEqualityComparer.Default.Equals(containingType, queryable)
                || SymbolEqualityComparer.Default.Equals(containingType, enumerable))
            && EfQueryOperationAnalysis.GetInvocationSource(invocation) is { } source
            && EfQueryOperationAnalysis.TryAnalyzeSource(
                EfQueryOperationAnalysis.Unwrap(source), queryable, dbSet, dbContext, efExtensions, out _))
        {
            suggestion = method.Name + "Async";
        }
        else if (method.Name == "SaveChanges"
            && EfQueryOperationAnalysis.IsOrDerivesFrom(invocation.Instance?.Type, dbContext))
        {
            suggestion = "SaveChangesAsync";
        }
        else if (method.Name == "Find"
            && EfQueryOperationAnalysis.IsOrDerivesFrom(invocation.Instance?.Type, dbSet))
        {
            suggestion = "FindAsync";
        }

        if (suggestion is null
            || !IsInAsyncContext(invocation, context.ContainingSymbol)
            || IsSyncArmOfAsyncSwitch(invocation, suggestion))
        {
            return;
        }

        var methodDisplay = invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var evidence = $"Invocation resolves to synchronous '{methodDisplay}' inside an async method; EF Core provides '{suggestion}' that can be awaited.";
        var remediation = $"Await '{suggestion}' instead of the synchronous call so the thread is released during the database round trip. A synchronous call off the hot path is acceptable; suppress with a recorded reason.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties, suggestion));
    }

    // `async ? await q.FirstOrDefaultAsync(p) : q.FirstOrDefault(p)`: the author already awaits the
    // counterpart on the async path, and the synchronous arm runs only when asked for.
    private static bool IsSyncArmOfAsyncSwitch(IOperation call, string counterpart)
    {
        for (IOperation? current = call, parent = call.Parent; parent is not null; current = parent, parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return false;
            }

            if (parent is IConditionalOperation conditional && conditional.WhenFalse is not null)
            {
                var otherArm = current == conditional.WhenTrue ? conditional.WhenFalse
                    : current == conditional.WhenFalse ? conditional.WhenTrue
                    : null;
                if (otherArm is not null && AwaitsCounterpart(otherArm, counterpart))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AwaitsCounterpart(IOperation arm, string counterpart)
    {
        foreach (var awaited in DescendantsAndSelf(arm).OfType<IAwaitOperation>())
        {
            var operation = EfQueryOperationAnalysis.Unwrap(awaited.Operation);
            if (operation is IInvocationOperation { TargetMethod.Name: "ConfigureAwait", Instance: { } configured })
            {
                operation = EfQueryOperationAnalysis.Unwrap(configured);
            }

            if (operation is IInvocationOperation invocation && invocation.TargetMethod.Name == counterpart)
            {
                return true;
            }
        }

        return false;
    }

    // In `async ? await q.FirstAsync() : q.First()` the arm is the await itself.
    private static IEnumerable<IOperation> DescendantsAndSelf(IOperation operation)
    {
        yield return operation;
        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool IsInAsyncContext(IOperation operation, ISymbol? containingSymbol)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IAnonymousFunctionOperation anonymousFunction:
                    return anonymousFunction.Symbol.IsAsync;
                case ILocalFunctionOperation localFunction:
                    return localFunction.Symbol.IsAsync;
            }
        }

        return containingSymbol is IMethodSymbol { IsAsync: true };
    }
}
