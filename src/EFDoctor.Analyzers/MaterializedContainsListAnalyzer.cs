using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MaterializedContainsListAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD015";
    public const string RuleTitle = "Materialized key list used in another query's Contains";
    public const string Confidence = "advisory";
    public const string DocumentationKey = "EFD015";
    public const string Impact = "The first query brings every matching value to the client, and this query sends them all back as a parameter list. The list has no bound: it costs an extra round trip, grows with the data, and gives the database a list it can't estimate (or, on older EF Core versions and some providers, one query plan per list and a limit on parameters).";
    public const string Remediation = "Keep the first query as an IQueryable (remove ToList/ToArray) so EF Core composes a single statement with WHERE … IN (SELECT …), or express the filter as a join or a navigation. Keep the list when it is small and known to stay small, or when it is reused elsewhere.";

    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "'{0}' is loaded from another query only to filter this one; compose it as a subquery";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Reports a Contains call in an EF Core query whose collection is a local materialized from another unbounded query on the same DbContext and used for nothing else.",
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

        var enumerable = context.Compilation.GetTypeByMetadataName(EnumerableMetadataName);
        var queryable = context.Compilation.GetTypeByMetadataName(QueryableMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (enumerable is null || queryable is null || dbSet is null || dbContext is null || efExtensions is null)
        {
            return;
        }

        context.RegisterOperationBlockAction(blockContext =>
        {
            foreach (var root in blockContext.OperationBlocks)
            {
                var identity = new EfContextIdentity(root, dbContext);
                foreach (var contains in root.Descendants().OfType<IInvocationOperation>().Where(static invocation => invocation.TargetMethod.Name == "Contains"))
                {
                    Analyze(blockContext, root, identity, contains, enumerable, queryable, dbSet, dbContext, efExtensions);
                }
            }
        });
    }

    private static void Analyze(
        OperationBlockAnalysisContext context,
        IOperation root,
        EfContextIdentity identity,
        IInvocationOperation contains,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions)
    {
        if (!TrySplitContains(contains, out var collection, out var value)
            || EfQueryOperationAnalysis.Unwrap(collection) is not ILocalReferenceOperation read
            || !TryGetOuterQuery(contains, value, queryable, dbSet, dbContext, efExtensions, out var outer)
            || !identity.TryGetKey(identity.ContextOfDbSet(outer.Origin), out var outerKey, out var contextDisplay))
        {
            return;
        }

        // Every use of the local is a Contains collection, so it is never reassigned and its value
        // is the initializer wherever it is read, even inside the query's lambda.
        var references = root.Descendants().OfType<ILocalReferenceOperation>().Where(reference => SymbolEqualityComparer.Default.Equals(reference.Local, read.Local)).ToList();
        if (references.Any(static reference => !IsContainsCollection(reference)))
        {
            return;
        }

        var declarator = root.Descendants().OfType<IVariableDeclaratorOperation>().FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, read.Local));
        if (declarator?.Initializer?.Value is not { } initializer
            || !TryGetUnboundedMaterialization(initializer, enumerable, queryable, dbSet, dbContext, efExtensions, out var load, out var inner)
            || !identity.TryGetKey(identity.ContextOfDbSet(inner.Origin), out var innerKey, out _)
            || !SymbolEqualityComparer.Default.Equals(outerKey, innerKey))
        {
            return;
        }

        var evidence = $"Local '{read.Local.Name}' is loaded by '{load.Syntax}', a query on context '{contextDisplay}' with no Take or key bound, and is used only as the collection of Contains in queries on the same context.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, contains.Syntax.GetLocation(), properties, read.Local.Name));
    }

    // `list.Contains(value)` on an instance, or `Enumerable.Contains(list, value)`.
    private static bool TrySplitContains(IInvocationOperation contains, out IOperation collection, out IOperation value)
    {
        collection = null!;
        value = null!;
        if (contains.Instance is { } instance && contains.Arguments.Length == 1)
        {
            collection = instance;
            value = contains.Arguments[0].Value;
            return true;
        }

        if (contains.TargetMethod.IsExtensionMethod || contains.TargetMethod.IsStatic)
        {
            var ordered = contains.Arguments.OrderBy(static argument => argument.Parameter?.Ordinal).ToList();
            if (ordered.Count == 2)
            {
                collection = ordered[0].Value;
                value = ordered[1].Value;
                return true;
            }
        }

        return false;
    }

    private static bool IsContainsCollection(IOperation reference)
    {
        var current = reference;
        while (current.Parent is IConversionOperation)
        {
            current = current.Parent;
        }

        if (current.Parent is IInvocationOperation { TargetMethod.Name: "Contains" } instanceCall && ReferenceEquals(instanceCall.Instance, current))
        {
            return true;
        }

        return current.Parent is IArgumentOperation { Parent: IInvocationOperation { TargetMethod.Name: "Contains" } call } argument
            && (call.TargetMethod.IsExtensionMethod || call.TargetMethod.IsStatic)
            && argument.Parameter?.Ordinal == 0;
    }

    // The proven EF query whose Queryable operator receives the lambda that contains the call, when
    // the tested value reads a parameter of that lambda or of one nested in it.
    private static bool TryGetOuterQuery(
        IOperation contains,
        IOperation value,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        out EfQueryChainAnalysis outer)
    {
        outer = null!;
        var parameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
        for (var current = contains.Parent; current is not null; current = current.Parent)
        {
            if (current is not IAnonymousFunctionOperation lambda)
            {
                continue;
            }

            foreach (var parameter in lambda.Symbol.Parameters)
            {
                parameters.Add(parameter);
            }

            IOperation node = lambda;
            while (node.Parent is IConversionOperation or IDelegateCreationOperation)
            {
                node = node.Parent;
            }

            if (node.Parent is IArgumentOperation { Parent: IInvocationOperation consumer }
                && SymbolEqualityComparer.Default.Equals(EfQueryOperationAnalysis.NormalizeMethod(consumer.TargetMethod).ContainingType.OriginalDefinition, queryable)
                && EfQueryOperationAnalysis.GetInvocationSource(consumer) is { } source
                && EfQueryOperationAnalysis.TryAnalyzeSource(source, queryable, dbSet, dbContext, efExtensions, out outer, followComposedLocals: true))
            {
                return value.DescendantsAndSelf().Any(operation => operation is IParameterReferenceOperation reference && parameters.Contains(reference.Parameter));
            }
        }

        return false;
    }

    private static bool TryGetUnboundedMaterialization(
        IOperation initializer,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        out IInvocationOperation load,
        out EfQueryChainAnalysis inner)
    {
        load = null!;
        inner = null!;
        var value = EfQueryOperationAnalysis.Unwrap(initializer);
        if (value is IAwaitOperation awaited)
        {
            value = EfQueryOperationAnalysis.Unwrap(awaited.Operation);
        }

        if (value is not IInvocationOperation invocation)
        {
            return false;
        }

        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        var containingType = method.ContainingType.OriginalDefinition;
        var materializes =
            (SymbolEqualityComparer.Default.Equals(containingType, enumerable) && method.Name is "ToList" or "ToArray" or "ToHashSet")
            || (SymbolEqualityComparer.Default.Equals(containingType, efExtensions) && method.Name is "ToListAsync" or "ToArrayAsync" or "ToHashSetAsync");
        if (!materializes
            || EfQueryOperationAnalysis.GetInvocationSource(invocation) is not { } source
            || !EfQueryOperationAnalysis.TryAnalyzeSource(source, queryable, dbSet, dbContext, efExtensions, out inner, followComposedLocals: true)
            || inner.RowBound.Strength is RowBoundStrength.Strong or RowBoundStrength.Medium)
        {
            return false;
        }

        load = invocation;
        return true;
    }
}
