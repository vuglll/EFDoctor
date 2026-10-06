using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaleTrackedEntitiesAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD038";
    public const string RuleTitle = "Bulk operation leaves tracked entities stale";
    public const string Confidence = "medium";
    public const string DocumentationKey = "EFD038";
    public const string Impact = "ExecuteUpdate and ExecuteDelete run directly in the database and bypass the change tracker, so entities of this type that the context already tracks keep their old values. Reading them, or loading them again from the same context, silently returns stale data, and saving one can overwrite the bulk update or fail because the row is gone. Whether a loaded entity is affected depends on the rows the bulk operation matches.";
    public const string Remediation = "Run the bulk operation before loading the entities, or load them with AsNoTracking when they are only read. Otherwise call ChangeTracker.Clear() after the bulk operation, reload or detach the affected entities with Entry(entity).Reload() or EntityState.Detached, or run the bulk operation on its own context. If the bulk operation's filter cannot match the loaded entities, suppress EFD038 with a recorded reason.";

    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string RelationalExtensionsMetadataName = "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    private const string Message = "This {0} bypasses the change tracker, so the '{1}' entities already tracked through '{2}' are stale";

    private static readonly ImmutableHashSet<string> BulkMethods =
        ImmutableHashSet.Create(StringComparer.Ordinal, "ExecuteUpdate", "ExecuteUpdateAsync", "ExecuteDelete", "ExecuteDeleteAsync");

    private static readonly ImmutableHashSet<string> LoadTerminals =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "First",
            "FirstOrDefault",
            "Single",
            "SingleOrDefault",
            "Last",
            "LastOrDefault",
            "ToList",
            "ToArray");

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Correctness",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports an EF Core ExecuteUpdate or ExecuteDelete that runs after entities of the same type were loaded with tracking into a local from the same DbContext instance.",
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

        // EF Core 7 and 8 declare the bulk operations on the relational extensions type.
        var relationalExtensions = context.Compilation.GetTypeByMetadataName(RelationalExtensionsMetadataName);
        var symbols = new Symbols(enumerable, queryable, dbSet, dbContext, efExtensions, relationalExtensions);
        context.RegisterOperationBlockAction(blockContext =>
        {
            foreach (var root in blockContext.OperationBlocks)
            {
                new BlockAnalysis(blockContext, symbols, root).Run();
            }
        });
    }

    private sealed class BlockAnalysis
    {
        private readonly OperationBlockAnalysisContext _context;
        private readonly Symbols _symbols;
        private readonly IOperation _root;
        private readonly EfContextIdentity _identity;

        public BlockAnalysis(OperationBlockAnalysisContext context, Symbols symbols, IOperation root)
        {
            _context = context;
            _symbols = symbols;
            _root = root;
            _identity = new EfContextIdentity(root, symbols.DbContext);
        }

        public void Run()
        {
            foreach (var operation in _root.DescendantsAndSelf())
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
                if (operation is IInvocationOperation invocation && IsBulkOperation(invocation))
                {
                    AnalyzeBulkOperation(invocation);
                }
            }
        }

        private bool IsBulkOperation(IInvocationOperation invocation)
        {
            var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
            if (!BulkMethods.Contains(method.Name))
            {
                return false;
            }

            var containingType = method.ContainingType.OriginalDefinition;
            return SymbolEqualityComparer.Default.Equals(containingType, _symbols.EfExtensions)
                || SymbolEqualityComparer.Default.Equals(containingType, _symbols.RelationalExtensions);
        }

        private void AnalyzeBulkOperation(IInvocationOperation bulk)
        {
            var source = EfQueryOperationAnalysis.GetInvocationSource(bulk);
            if (source is null
                || !TryGetQueryTarget(source, out var entity, out var contextKey, out var contextDisplay, out _, out _)
                || !TryFindLoad(bulk, entity, contextKey, out var load))
            {
                return;
            }

            var use = FindUseAfter(bulk, load, entity, contextKey);
            var entityName = entity.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var bulkName = bulk.TargetMethod.Name;
            var action = bulkName.StartsWith("ExecuteDelete", StringComparison.Ordinal) ? "deletes" : "updates";
            var evidence = $"Local '{load.Local.Name}' is loaded with tracking by '{load.Invocation.Syntax}' on context '{contextDisplay}'. {bulkName} then {action} '{entityName}' rows directly in the database on the same context, without updating the change tracker, so '{load.Local.Name}' keeps the state it was loaded with. "
                + (use ?? $"Nothing later in this method uses '{load.Local.Name}', loads '{entityName}' again, or saves a change to it, so the stale entities are not observed here, but they stay tracked for the rest of the context's lifetime.");
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(DiagnosticPropertyNames.Confidence, use is null ? Confidence : "high")
                .Add(DiagnosticPropertyNames.Evidence, evidence)
                .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
                .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
                .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

            _context.ReportDiagnostic(EfDiagnostic.Create(Rule, bulk.Syntax.GetLocation(), properties, bulkName, entityName, load.Local.Name));
        }

        // The closest tracked load of the entity type on the same context that is a statement of a
        // block enclosing the bulk operation, and comes before the statement that contains it.
        // Every path that reaches the bulk operation has then run the load.
        private bool TryFindLoad(IInvocationOperation bulk, INamedTypeSymbol entity, ISymbol contextKey, out TrackedLoad load)
        {
            IOperation child = bulk;
            for (var parent = bulk.Parent; parent is not null; child = parent, parent = parent.Parent)
            {
                if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
                {
                    break;
                }

                if (parent is not IBlockOperation block)
                {
                    continue;
                }

                var bulkIndex = block.Operations.IndexOf(child);
                for (var index = bulkIndex - 1; index >= 0; index--)
                {
                    if (block.Operations[index] is IVariableDeclarationGroupOperation { Declarations.Length: 1 } group
                        && group.Declarations[0].Declarators.Length == 1
                        && group.Declarations[0].Declarators[0] is { Initializer.Value: { } initializer } declarator
                        && TryGetTrackedLoad(initializer, out var invocation, out var loadedEntity, out var loadKey)
                        && SymbolEqualityComparer.Default.Equals(loadedEntity, entity)
                        && SymbolEqualityComparer.Default.Equals(loadKey, contextKey)
                        && !HaveDisjointFilters(invocation, bulk)
                        && !ManagesTrackerAfter(block, index, contextKey))
                    {
                        load = new TrackedLoad(declarator.Symbol, invocation, block, bulkIndex);
                        return true;
                    }
                }
            }

            load = null!;
            return false;
        }

        private bool TryGetTrackedLoad(IOperation value, out IInvocationOperation invocation, out INamedTypeSymbol entity, out ISymbol contextKey)
        {
            invocation = null!;
            entity = null!;
            contextKey = null!;
            value = EfQueryOperationAnalysis.Unwrap(value);
            if (value is IAwaitOperation awaited)
            {
                value = EfQueryOperationAnalysis.Unwrap(awaited.Operation);
            }

            if (value is not IInvocationOperation candidate)
            {
                return false;
            }

            invocation = candidate;
            var method = EfQueryOperationAnalysis.NormalizeMethod(candidate.TargetMethod);
            if (method.Name is "Find" or "FindAsync"
                && candidate.Instance is { } set
                && EfQueryOperationAnalysis.IsOrDerivesFrom(method.ContainingType, _symbols.DbSet))
            {
                return EntityOverFetchAnalyzer.TryGetEntityType(set, _symbols.DbSet, out entity)
                    && _identity.TryGetKey(_identity.ContextOfDbSet(set), out contextKey, out _);
            }

            var containingType = method.ContainingType.OriginalDefinition;
            var synchronous = SymbolEqualityComparer.Default.Equals(containingType, _symbols.Queryable)
                || SymbolEqualityComparer.Default.Equals(containingType, _symbols.Enumerable);
            var name = method.Name;
            if (!synchronous)
            {
                if (!SymbolEqualityComparer.Default.Equals(containingType, _symbols.EfExtensions)
                    || !name.EndsWith("Async", StringComparison.Ordinal))
                {
                    return false;
                }

                name = name.Substring(0, name.Length - "Async".Length);
            }

            var source = EfQueryOperationAnalysis.GetInvocationSource(candidate);
            return LoadTerminals.Contains(name)
                && source is not null
                && TryGetQueryTarget(source, out entity, out contextKey, out _, out var elementType, out var tracked)
                && tracked
                && SymbolEqualityComparer.Default.Equals(elementType, entity);
        }

        // The entity type and context of a proven query, the element type it yields at this point,
        // and whether its results are tracked.
        private bool TryGetQueryTarget(
            IOperation source,
            out INamedTypeSymbol entity,
            out ISymbol contextKey,
            out string contextDisplay,
            out ITypeSymbol? elementType,
            out bool tracked)
        {
            entity = null!;
            contextKey = null!;
            contextDisplay = null!;
            elementType = null;
            tracked = false;
            if (!EfQueryOperationAnalysis.TryAnalyzeSource(source, _symbols.Queryable, _symbols.DbSet, _symbols.DbContext, _symbols.EfExtensions, out var analysis)
                || !EntityOverFetchAnalyzer.TryGetEntityType(analysis.Origin, _symbols.DbSet, out entity)
                || !_identity.TryGetKey(_identity.ContextOfDbSet(analysis.Origin), out contextKey, out contextDisplay))
            {
                return false;
            }

            elementType = GetElementType(EfQueryOperationAnalysis.Unwrap(source).Type);
            tracked = !analysis.Operations.Any(static operation => operation is "EF.AsNoTracking" or "EF.AsNoTrackingWithIdentityResolution");
            return true;
        }

        // ChangeTracker.Clear(), Entry(entity).Reload(), or a detach on the same context, anywhere
        // after the load, shows the tracker state is being managed.
        private bool ManagesTrackerAfter(IBlockOperation block, int loadIndex, ISymbol contextKey)
        {
            foreach (var operation in block.Operations.Skip(loadIndex + 1).SelectMany(static statement => statement.DescendantsAndSelf()))
            {
                switch (operation)
                {
                    case IInvocationOperation { TargetMethod.Name: "Clear", Instance: IPropertyReferenceOperation { Property.Name: "ChangeTracker", Instance: { } context } }
                        when IsContext(context, contextKey):
                    case IInvocationOperation { TargetMethod.Name: "Reload" or "ReloadAsync", Instance: IInvocationOperation { TargetMethod.Name: "Entry", Instance: { } entryContext } }
                        when IsContext(entryContext, contextKey):
                    case ISimpleAssignmentOperation
                    {
                        Target: IPropertyReferenceOperation { Property.Name: "State", Instance: IInvocationOperation { TargetMethod.Name: "Entry", Instance: { } stateContext } },
                        Value: var state,
                    }
                        when IsContext(stateContext, contextKey)
                            && EfQueryOperationAnalysis.Unwrap(state) is IFieldReferenceOperation { Field.Name: "Detached" }:
                        return true;
                }
            }

            return false;
        }

        // Two filters can't match the same row when both require the same property to equal a
        // constant, and the constants differ: a load of the setting named "A" is not made stale by
        // a bulk delete of the setting named "B".
        private static bool HaveDisjointFilters(IInvocationOperation load, IInvocationOperation bulk)
        {
            var loadFilter = ConstantEqualities(load);
            if (loadFilter.Count == 0)
            {
                return false;
            }

            foreach (var equality in ConstantEqualities(bulk))
            {
                if (loadFilter.TryGetValue(equality.Key, out var value) && !Equals(value, equality.Value))
                {
                    return true;
                }
            }

            return false;
        }

        // `x.Property == constant` conjuncts of the predicates in an inline query chain.
        private static Dictionary<ISymbol, object?> ConstantEqualities(IInvocationOperation terminal)
        {
            var equalities = new Dictionary<ISymbol, object?>(SymbolEqualityComparer.Default);
            IOperation? current = terminal;
            while (current is not null && EfQueryOperationAnalysis.Unwrap(current) is IInvocationOperation invocation)
            {
                foreach (var argument in invocation.Arguments)
                {
                    if (EfQueryOperationAnalysis.TryGetLambda(argument.Value, out var lambda)
                        && lambda.Symbol.Parameters.Length == 1
                        && lambda.Symbol.ReturnType.SpecialType == SpecialType.System_Boolean
                        && EfQueryOperationAnalysis.GetLambdaExpression(lambda) is { } body)
                    {
                        CollectConstantEqualities(body, lambda.Symbol.Parameters[0], equalities);
                    }
                }

                current = EfQueryOperationAnalysis.GetInvocationSource(invocation);
            }

            return equalities;
        }

        private static void CollectConstantEqualities(IOperation condition, IParameterSymbol parameter, Dictionary<ISymbol, object?> equalities)
        {
            condition = EfQueryOperationAnalysis.Unwrap(condition);
            if (condition is not IBinaryOperation binary)
            {
                return;
            }

            if (binary.OperatorKind == BinaryOperatorKind.ConditionalAnd)
            {
                CollectConstantEqualities(binary.LeftOperand, parameter, equalities);
                CollectConstantEqualities(binary.RightOperand, parameter, equalities);
                return;
            }

            if (binary.OperatorKind != BinaryOperatorKind.Equals)
            {
                return;
            }

            var left = EfQueryOperationAnalysis.Unwrap(binary.LeftOperand);
            var right = EfQueryOperationAnalysis.Unwrap(binary.RightOperand);
            if (TryGetParameterProperty(left, parameter, out var property) && right.ConstantValue.HasValue)
            {
                equalities[property] = right.ConstantValue.Value;
            }
            else if (TryGetParameterProperty(right, parameter, out property) && left.ConstantValue.HasValue)
            {
                equalities[property] = left.ConstantValue.Value;
            }
        }

        private static bool TryGetParameterProperty(IOperation operation, IParameterSymbol parameter, out ISymbol property)
        {
            if (operation is IPropertyReferenceOperation { Instance: IParameterReferenceOperation instance } reference
                && SymbolEqualityComparer.Default.Equals(instance.Parameter, parameter))
            {
                property = reference.Property.OriginalDefinition;
                return true;
            }

            property = null!;
            return false;
        }

        private bool IsContext(IOperation expression, ISymbol contextKey) =>
            EfQueryOperationAnalysis.IsOrDerivesFrom(expression.Type, _symbols.DbContext)
            && _identity.TryGetKey(expression, out var key, out _)
            && SymbolEqualityComparer.Default.Equals(key, contextKey);

        // What the method does with the stale state after the bulk operation, if anything.
        private string? FindUseAfter(IInvocationOperation bulk, TrackedLoad load, INamedTypeSymbol entity, ISymbol contextKey)
        {
            var bulkEnd = bulk.Syntax.Span.End;
            var modified = IsModified(load);
            var later = load.Block.Operations
                .Skip(load.BulkStatementIndex)
                .SelectMany(DescendantsOutsideFunctions)
                .Where(operation => operation.Syntax.SpanStart >= bulkEnd)
                .OrderBy(static operation => operation.Syntax.SpanStart);
            foreach (var operation in later)
            {
                switch (operation)
                {
                    case ILocalReferenceOperation reference when SymbolEqualityComparer.Default.Equals(reference.Local, load.Local):
                        return $"The method then uses '{load.Local.Name}' again on line {Line(reference)}.";

                    case IInvocationOperation { TargetMethod.Name: "SaveChanges" or "SaveChangesAsync", Instance: { } instance } save
                        when modified && EfAsyncOperations.IsDbContextMethod(save.TargetMethod, _symbols.DbContext) && IsContext(instance, contextKey):
                        return $"The method modifies '{load.Local.Name}' and calls {save.TargetMethod.Name} on the same context on line {Line(save)}, after the bulk operation, so stale state is written back.";

                    case IInvocationOperation invocation
                        when TryGetTrackedLoad(invocation, out var again, out var loadedEntity, out var loadKey)
                            && ReferenceEquals(again, invocation)
                            && SymbolEqualityComparer.Default.Equals(loadedEntity, entity)
                            && SymbolEqualityComparer.Default.Equals(loadKey, contextKey):
                        return $"The method then loads '{entity.Name}' again with '{invocation.Syntax}' on line {Line(invocation)}, which returns the instances the context already tracks, not the database's current values.";
                }
            }

            return null;
        }

        // The T of the IEnumerable<T> a query type implements; IIncludableQueryable and similar
        // types have more than one type argument.
        private static ITypeSymbol? GetElementType(ITypeSymbol? type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T } enumerable)
            {
                return enumerable.TypeArguments[0];
            }

            return type?.AllInterfaces
                .FirstOrDefault(static candidate => candidate.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                ?.TypeArguments[0];
        }

        // A member of the loaded entity, or of an entity taken from the loaded collection by a
        // foreach or an index, is written somewhere after the load.
        private static bool IsModified(TrackedLoad load)
        {
            var entities = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { load.Local };
            var operations = load.Block.Operations.SelectMany(static statement => statement.DescendantsAndSelf()).ToList();
            foreach (var operation in operations)
            {
                if (operation is IForEachLoopOperation { LoopControlVariable: IVariableDeclaratorOperation variable } loop
                    && EfQueryOperationAnalysis.Unwrap(loop.Collection) is ILocalReferenceOperation collection
                    && SymbolEqualityComparer.Default.Equals(collection.Local, load.Local))
                {
                    entities.Add(variable.Symbol);
                }
            }

            foreach (var operation in operations)
            {
                var target = operation switch
                {
                    IAssignmentOperation assignment => assignment.Target,
                    IIncrementOrDecrementOperation increment => increment.Target,
                    _ => null,
                };
                var instance = target switch
                {
                    IPropertyReferenceOperation property => property.Instance,
                    IFieldReferenceOperation field => field.Instance,
                    _ => null,
                };

                // `list[0].Price = 1` writes through an index read of the loaded collection.
                if (instance is IPropertyReferenceOperation { Property.IsIndexer: true, Instance: { } indexed })
                {
                    instance = indexed;
                }
                else if (instance is IArrayElementReferenceOperation element)
                {
                    instance = element.ArrayReference;
                }

                if (instance is not null
                    && EfQueryOperationAnalysis.Unwrap(instance) is ILocalReferenceOperation reference
                    && entities.Contains(reference.Local))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<IOperation> DescendantsOutsideFunctions(IOperation operation)
        {
            yield return operation;
            foreach (var child in operation.ChildOperations)
            {
                if (child is IAnonymousFunctionOperation or ILocalFunctionOperation)
                {
                    continue;
                }

                foreach (var descendant in DescendantsOutsideFunctions(child))
                {
                    yield return descendant;
                }
            }
        }

        private static int Line(IOperation operation) =>
            operation.Syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }

    private sealed class TrackedLoad
    {
        public TrackedLoad(ILocalSymbol local, IInvocationOperation invocation, IBlockOperation block, int bulkStatementIndex)
        {
            Local = local;
            Invocation = invocation;
            Block = block;
            BulkStatementIndex = bulkStatementIndex;
        }

        public ILocalSymbol Local { get; }

        public IInvocationOperation Invocation { get; }

        public IBlockOperation Block { get; }

        public int BulkStatementIndex { get; }
    }

    private sealed class Symbols
    {
        public Symbols(
            INamedTypeSymbol enumerable,
            INamedTypeSymbol queryable,
            INamedTypeSymbol dbSet,
            INamedTypeSymbol dbContext,
            INamedTypeSymbol efExtensions,
            INamedTypeSymbol? relationalExtensions)
        {
            Enumerable = enumerable;
            Queryable = queryable;
            DbSet = dbSet;
            DbContext = dbContext;
            EfExtensions = efExtensions;
            RelationalExtensions = relationalExtensions;
        }

        public INamedTypeSymbol Enumerable { get; }

        public INamedTypeSymbol Queryable { get; }

        public INamedTypeSymbol DbSet { get; }

        public INamedTypeSymbol DbContext { get; }

        public INamedTypeSymbol EfExtensions { get; }

        public INamedTypeSymbol? RelationalExtensions { get; }
    }
}
