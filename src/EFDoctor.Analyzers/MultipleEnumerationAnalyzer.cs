using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MultipleEnumerationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD020";
    public const string RuleTitle = "Query is enumerated more than once";
    public const string Confidence = "medium";
    public const string DocumentationKey = "EFD020";
    public const string Impact = "Each enumeration of a deferred IQueryable re-executes the query and issues another database round trip; the extra work grows with how often the code runs.";

    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string IQueryableMetadataName = "System.Linq.IQueryable`1";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "This EF Core query is enumerated more than once; each enumeration re-executes it against the database";
    private const string Remediation = "Materialize the query once — for example into a List<T> with ToList or ToListAsync — and reuse the result. If you intend to re-query to observe changed data, suppress with a recorded reason.";

    private static readonly ImmutableHashSet<string> TerminalMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ToList", "ToListAsync", "ToArray", "ToArrayAsync", "ToDictionary", "ToDictionaryAsync", "ToHashSet", "ToHashSetAsync",
        "Count", "CountAsync", "LongCount", "LongCountAsync",
        "Any", "AnyAsync", "All", "AllAsync",
        "First", "FirstAsync", "FirstOrDefault", "FirstOrDefaultAsync",
        "Single", "SingleAsync", "SingleOrDefault", "SingleOrDefaultAsync",
        "Last", "LastAsync", "LastOrDefault", "LastOrDefaultAsync",
        "Min", "MinAsync", "Max", "MaxAsync", "Sum", "SumAsync", "Average", "AverageAsync",
        "Contains", "ContainsAsync", "ElementAt", "ElementAtAsync", "ElementAtOrDefault");

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports an EF Core IQueryable local that is executed more than once on the same path through a method; uses composed into another query's expression tree and predicate terminals are not counted.");

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
        var iqueryable = context.Compilation.GetTypeByMetadataName(IQueryableMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (queryable is null || iqueryable is null || dbSet is null || dbContext is null || efExtensions is null)
        {
            return;
        }

        context.RegisterOperationBlockAction(
            blockContext => AnalyzeBlocks(blockContext, queryable, iqueryable, dbSet, dbContext, efExtensions));
    }

    private static void AnalyzeBlocks(
        OperationBlockAnalysisContext context,
        INamedTypeSymbol queryable,
        INamedTypeSymbol iqueryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions)
    {
        var candidates = new Dictionary<ILocalSymbol, IVariableDeclaratorOperation>(SymbolEqualityComparer.Default);
        var reassigned = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in DescendantsAndSelf(block))
            {
                if (operation is IVariableDeclaratorOperation declarator
                    && declarator.Initializer?.Value is { } initializer
                    && ImplementsIQueryable(declarator.Symbol.Type, iqueryable)
                    && EfQueryOperationAnalysis.TryAnalyzeSource(
                        EfQueryOperationAnalysis.Unwrap(initializer), queryable, dbSet, dbContext, efExtensions, out _))
                {
                    candidates[declarator.Symbol] = declarator;
                }
                else if (operation is ISimpleAssignmentOperation assignment
                    && TryGetLocal(assignment.Target, out var assignedLocal))
                {
                    reassigned.Add(assignedLocal);
                }
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var executions = new Dictionary<ILocalSymbol, List<IOperation>>(SymbolEqualityComparer.Default);
        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in DescendantsAndSelf(block))
            {
                if (operation is IForEachLoopOperation loop
                    && TryGetLocal(loop.Collection, out var loopLocal)
                    && candidates.ContainsKey(loopLocal))
                {
                    AddExecution(executions, loopLocal, operation);
                }
                else if (operation is IInvocationOperation invocation
                    && IsTerminal(invocation)
                    && !HasPredicate(invocation)
                    && TryGetLocal(EfQueryOperationAnalysis.GetInvocationSource(invocation), out var sourceLocal)
                    && candidates.ContainsKey(sourceLocal))
                {
                    AddExecution(executions, sourceLocal, operation);
                }
            }
        }

        foreach (var candidate in candidates)
        {
            var local = candidate.Key;
            if (reassigned.Contains(local) || !executions.TryGetValue(local, out var uses))
            {
                continue;
            }

            var count = MaxExecutionsOnOnePath(uses);
            if (count < 2)
            {
                continue;
            }

            var evidence = $"Local '{local.Name}' is an EF Core query traced to a DbSet origin and is enumerated {count} times in this method; each enumeration re-executes the query.";
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(DiagnosticPropertyNames.Confidence, Confidence)
                .Add(DiagnosticPropertyNames.Evidence, evidence)
                .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
                .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
                .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

            context.ReportDiagnostic(Diagnostic.Create(Rule, candidate.Value.Syntax.GetLocation(), properties));
        }
    }

    // A predicate overload (`q.Any(x => ...)`) runs a different query over the same base, so it
    // is not a re-execution of the local. Selector overloads (`q.Sum(x => x.Total)`) still are.
    private static bool HasPredicate(IInvocationOperation invocation) =>
        invocation.Arguments.Any(static argument => argument.Parameter?.Name == "predicate");

    // A use inside a lambda converted to an expression tree, including one nested in a delegate
    // lambda within that tree (`e => e.Items.Any(i => ids.Contains(i.Id))`), is composed into the
    // enclosing query as a subquery; it never executes on its own.
    private static bool IsInsideExpressionTree(IOperation operation)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation
                && current.Parent is { Type: INamedTypeSymbol converted }
                && converted.OriginalDefinition.ToDisplayString() == "System.Linq.Expressions.Expression<TDelegate>")
            {
                return true;
            }
        }

        return false;
    }

    private static void AddExecution(Dictionary<ILocalSymbol, List<IOperation>> executions, ILocalSymbol local, IOperation operation)
    {
        if (IsInsideExpressionTree(operation))
        {
            return;
        }

        if (!executions.TryGetValue(local, out var list))
        {
            executions[local] = list = new List<IOperation>();
        }

        list.Add(operation);
    }

    // Executions in the two arms of one conditional are mutually exclusive, so the count is the
    // largest number of executions that can run on a single path through the method.
    private static int MaxExecutionsOnOnePath(IReadOnlyList<IOperation> executions)
    {
        if (executions.Count < 2)
        {
            return executions.Count;
        }

        foreach (var execution in executions)
        {
            for (var current = execution.Parent; current is not null; current = current.Parent)
            {
                if (current is not IConditionalOperation conditional || conditional.WhenFalse is null)
                {
                    continue;
                }

                var whenTrue = executions.Where(candidate => IsWithin(candidate, conditional.WhenTrue)).ToList();
                var whenFalse = executions.Where(candidate => IsWithin(candidate, conditional.WhenFalse)).ToList();
                if (whenTrue.Count == 0 || whenFalse.Count == 0)
                {
                    continue;
                }

                var outside = executions.Except(whenTrue).Except(whenFalse).ToList();
                return Math.Max(
                    MaxExecutionsOnOnePath(outside.Concat(whenTrue).ToList()),
                    MaxExecutionsOnOnePath(outside.Concat(whenFalse).ToList()));
            }
        }

        return executions.Count;
    }

    private static bool IsWithin(IOperation operation, IOperation ancestor)
    {
        for (var current = operation; current is not null; current = current.Parent)
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTerminal(IInvocationOperation invocation) =>
        TerminalMethods.Contains(EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod).Name);

    private static bool ImplementsIQueryable(ITypeSymbol type, INamedTypeSymbol iqueryable)
    {
        if (type is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, iqueryable))
        {
            return true;
        }

        return type.AllInterfaces.Any(@interface =>
            SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, iqueryable));
    }

    private static bool TryGetLocal(IOperation? operation, out ILocalSymbol local)
    {
        local = null!;
        if (operation is null)
        {
            return false;
        }

        if (EfQueryOperationAnalysis.Unwrap(operation) is ILocalReferenceOperation reference)
        {
            local = reference.Local;
            return true;
        }

        return false;
    }

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
}
