using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UntrackedChangeLostAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD032";
    public const string RuleTitle = "Change to an untracked entity is never saved";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD032";
    public const string Impact = "An entity loaded with AsNoTracking is not tracked by the context, so SaveChanges detects no change to it and writes nothing. The method succeeds and no error is raised: the update is silently lost.";
    public const string Remediation = "Remove AsNoTracking from a load that is meant for an update, or call Update(entity) or Attach(entity) on the context before changing it, or replace the load-change-save sequence with ExecuteUpdate. If an interceptor or SaveChanges override attaches the entity, suppress EFD032 with a recorded reason.";

    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "'{0}' was loaded with {1}, so SaveChanges does not save this change";

    private static readonly ImmutableHashSet<string> SingleTerminals =
        ImmutableHashSet.Create(StringComparer.Ordinal, "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault");

    private static readonly ImmutableHashSet<string> ListTerminals =
        ImmutableHashSet.Create(StringComparer.Ordinal, "ToList", "ToArray");

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Correctness",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports a property change to an entity loaded with AsNoTracking into a local, followed by SaveChanges on the same DbContext instance with nothing attaching the entity in between.",
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

        var symbols = new Symbols(enumerable, queryable, dbSet, dbContext, efExtensions);
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
        private readonly HashSet<ILocalSymbol> _reported = new(SymbolEqualityComparer.Default);

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
                if (operation is IInvocationOperation { TargetMethod.Name: "SaveChanges" or "SaveChangesAsync", Instance: { } instance } save
                    && EfAsyncOperations.IsDbContextMethod(save.TargetMethod, _symbols.DbContext)
                    && _identity.TryGetKey(instance, out var contextKey, out var contextDisplay))
                {
                    AnalyzeSave(save, contextKey, contextDisplay);
                }
            }
        }

        // Each untracked load on the save's context that is an earlier statement of a block
        // enclosing the save, in the same function, with a change and no attaching use between.
        private void AnalyzeSave(IInvocationOperation save, ISymbol contextKey, string contextDisplay)
        {
            IOperation child = save;
            for (var parent = save.Parent; parent is not null; child = parent, parent = parent.Parent)
            {
                if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
                {
                    return;
                }

                if (parent is not IBlockOperation block)
                {
                    continue;
                }

                var saveIndex = block.Operations.IndexOf(child);
                for (var loadIndex = saveIndex - 1; loadIndex >= 0; loadIndex--)
                {
                    if (block.Operations[loadIndex] is IVariableDeclarationGroupOperation { Declarations.Length: 1 } group
                        && group.Declarations[0].Declarators.Length == 1
                        && group.Declarations[0].Declarators[0] is { Initializer.Value: { } initializer } declarator
                        && !_reported.Contains(declarator.Symbol)
                        && TryGetUntrackedLoad(initializer, contextKey, out var load, out var trackingMethod, out var isList))
                    {
                        var between = block.Operations.Skip(loadIndex + 1).Take(saveIndex - loadIndex).ToList();
                        if (TryFindLostChange(declarator.Symbol, isList, between, out var change, out var entity)
                            && _reported.Add(declarator.Symbol))
                        {
                            Report(change, entity, declarator.Symbol, isList, load, trackingMethod, contextDisplay, save);
                        }
                    }
                }
            }
        }

        private bool TryGetUntrackedLoad(IOperation value, ISymbol contextKey, out IInvocationOperation load, out string trackingMethod, out bool isList)
        {
            load = null!;
            trackingMethod = null!;
            isList = false;
            value = EfQueryOperationAnalysis.Unwrap(value);
            if (value is IAwaitOperation awaited)
            {
                value = EfQueryOperationAnalysis.Unwrap(awaited.Operation);
            }

            if (value is not IInvocationOperation candidate)
            {
                return false;
            }

            var method = EfQueryOperationAnalysis.NormalizeMethod(candidate.TargetMethod);
            var containingType = method.ContainingType.OriginalDefinition;
            var name = method.Name;
            if (SymbolEqualityComparer.Default.Equals(containingType, _symbols.EfExtensions) && name.EndsWith("Async", StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - "Async".Length);
            }
            else if (!SymbolEqualityComparer.Default.Equals(containingType, _symbols.Queryable)
                && !SymbolEqualityComparer.Default.Equals(containingType, _symbols.Enumerable))
            {
                return false;
            }

            isList = ListTerminals.Contains(name);
            if (!isList && !SingleTerminals.Contains(name))
            {
                return false;
            }

            if (EfQueryOperationAnalysis.GetInvocationSource(candidate) is not { } source
                || !EfQueryOperationAnalysis.TryAnalyzeSource(source, _symbols.Queryable, _symbols.DbSet, _symbols.DbContext, _symbols.EfExtensions, out var analysis)
                || !_identity.TryGetKey(_identity.ContextOfDbSet(analysis.Origin), out var loadKey, out _)
                || !SymbolEqualityComparer.Default.Equals(loadKey, contextKey))
            {
                return false;
            }

            var noTracking = analysis.Operations.LastOrDefault(static operation => operation is "EF.AsNoTracking" or "EF.AsNoTrackingWithIdentityResolution" or "EF.AsTracking");
            if (noTracking is null or "EF.AsTracking")
            {
                return false;
            }

            load = candidate;
            trackingMethod = noTracking.Substring("EF.".Length);
            return true;
        }

        // The first property change to the entity, when every reference to the loaded local between
        // the load and the save is a property access (or, for a list, a foreach over it whose
        // iteration variable is only accessed through its properties).
        private static bool TryFindLostChange(ILocalSymbol loaded, bool isList, List<IOperation> between, out IOperation change, out ILocalSymbol entity)
        {
            change = null!;
            entity = loaded;
            var operations = between.SelectMany(static statement => statement.DescendantsAndSelf()).ToList();
            var entityLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            if (isList)
            {
                foreach (var loop in operations.OfType<IForEachLoopOperation>())
                {
                    if (EfQueryOperationAnalysis.Unwrap(loop.Collection) is ILocalReferenceOperation collection
                        && SymbolEqualityComparer.Default.Equals(collection.Local, loaded)
                        && loop.Locals.Length == 1)
                    {
                        entityLocals.Add(loop.Locals[0]);
                    }
                }

                if (entityLocals.Count == 0)
                {
                    return false;
                }
            }
            else
            {
                entityLocals.Add(loaded);
            }

            IOperation? first = null;
            foreach (var reference in operations.OfType<ILocalReferenceOperation>())
            {
                if (SymbolEqualityComparer.Default.Equals(reference.Local, loaded) && isList)
                {
                    if (!IsForEachCollection(reference) && reference.Parent is not IPropertyReferenceOperation)
                    {
                        return false;
                    }

                    continue;
                }

                if (!entityLocals.Contains(reference.Local))
                {
                    continue;
                }

                if (reference.IsDeclaration || IsInNestedFunction(reference, between))
                {
                    return false;
                }

                if (IsNullCheck(reference))
                {
                    continue;
                }

                if (reference.Parent is not IPropertyReferenceOperation property || !ReferenceEquals(property.Instance, reference))
                {
                    return false;
                }

                if (ChangeOf(property) is { } assignment
                    && (first is null || assignment.Syntax.SpanStart < first.Syntax.SpanStart))
                {
                    first = assignment;
                    entity = reference.Local;
                }
            }

            change = first!;
            return first is not null;
        }

        // `entity == null`, `entity != null`, `entity is null`, or `entity is not null`: a read that
        // can't attach the entity.
        private static bool IsNullCheck(IOperation reference)
        {
            var current = reference;
            while (current.Parent is IConversionOperation)
            {
                current = current.Parent;
            }

            return current.Parent switch
            {
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } binary =>
                    EfQueryOperationAnalysis.Unwrap(ReferenceEquals(binary.LeftOperand, current) ? binary.RightOperand : binary.LeftOperand).ConstantValue is { HasValue: true, Value: null },
                IIsPatternOperation pattern => ReferenceEquals(pattern.Value, current) && pattern.Pattern is IConstantPatternOperation or INegatedPatternOperation,
                _ => false,
            };
        }

        private static bool IsForEachCollection(IOperation reference)
        {
            var current = reference;
            while (current.Parent is IConversionOperation)
            {
                current = current.Parent;
            }

            return current.Parent is IForEachLoopOperation loop && ReferenceEquals(loop.Collection, current);
        }

        private static bool IsInNestedFunction(IOperation operation, List<IOperation> statements)
        {
            for (var current = operation.Parent; current is not null; current = current.Parent)
            {
                if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                {
                    return true;
                }

                if (statements.Contains(current))
                {
                    return false;
                }
            }

            return false;
        }

        private static IOperation? ChangeOf(IPropertyReferenceOperation property) => property.Parent switch
        {
            ISimpleAssignmentOperation assignment when ReferenceEquals(assignment.Target, property) => assignment,
            ICompoundAssignmentOperation compound when ReferenceEquals(compound.Target, property) => compound,
            ICoalesceAssignmentOperation coalesce when ReferenceEquals(coalesce.Target, property) => coalesce,
            IIncrementOrDecrementOperation increment => increment,
            _ => null,
        };

        private void Report(
            IOperation change,
            ILocalSymbol entity,
            ILocalSymbol loaded,
            bool isList,
            IInvocationOperation load,
            string trackingMethod,
            string contextDisplay,
            IInvocationOperation save)
        {
            var saveLine = save.Syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var subject = isList ? $"Each element '{entity.Name}' of local '{loaded.Name}'" : $"Local '{loaded.Name}'";
            var evidence = $"{subject} is loaded by '{load.Syntax}' with {trackingMethod} on context '{contextDisplay}'. The method then changes it and calls {save.TargetMethod.Name} on the same context on line {saveLine}, and nothing in between attaches it, so the change tracker never sees the change.";
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(DiagnosticPropertyNames.Confidence, Confidence)
                .Add(DiagnosticPropertyNames.Evidence, evidence)
                .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
                .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
                .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

            _context.ReportDiagnostic(EfDiagnostic.Create(Rule, change.Syntax.GetLocation(), properties, entity.Name, trackingMethod));
        }
    }

    private sealed class Symbols
    {
        public Symbols(INamedTypeSymbol enumerable, INamedTypeSymbol queryable, INamedTypeSymbol dbSet, INamedTypeSymbol dbContext, INamedTypeSymbol efExtensions)
        {
            Enumerable = enumerable;
            Queryable = queryable;
            DbSet = dbSet;
            DbContext = dbContext;
            EfExtensions = efExtensions;
        }

        public INamedTypeSymbol Enumerable { get; }

        public INamedTypeSymbol Queryable { get; }

        public INamedTypeSymbol DbSet { get; }

        public INamedTypeSymbol DbContext { get; }

        public INamedTypeSymbol EfExtensions { get; }
    }
}
