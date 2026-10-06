using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConcurrentDbContextOperationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD027";
    public const string RuleTitle = "Concurrent operations on one DbContext";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD027";
    public const string Impact = "A DbContext is not thread-safe and supports one operation at a time. When a second operation starts on the same instance before the first completes, EF Core throws InvalidOperationException (\"A second operation was started on this context instance before a previous operation completed\"). Whether it throws depends on timing, so the failure can stay hidden in tests and appear under production latency.";
    public const string Remediation = "Await each operation before starting the next one on the same context. If the operations must run concurrently, give each one its own context, for example by creating it with IDbContextFactory<TContext>.CreateDbContext() inside the concurrent work and disposing it there.";

    private const string TaskMetadataName = "System.Threading.Tasks.Task";
    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "This EF Core operation starts while another operation on the same DbContext is still pending";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Reliability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports an EF Core asynchronous operation that starts while another operation on the same, symbol-proven DbContext instance is pending: operations passed together to Task.WhenAll or Task.WhenAny, an operation started before an earlier task local is observed, or an operation projected by Enumerable.Select into a task combinator over a captured context.",
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

        var task = context.Compilation.GetTypeByMetadataName(TaskMetadataName);
        var enumerable = context.Compilation.GetTypeByMetadataName(EnumerableMetadataName);
        var queryable = context.Compilation.GetTypeByMetadataName(QueryableMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (task is null || enumerable is null || queryable is null || dbSet is null || dbContext is null || efExtensions is null)
        {
            return;
        }

        var symbols = new Symbols(task, enumerable, queryable, dbSet, dbContext, efExtensions);
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
        private readonly Dictionary<ISymbol, bool> _neverWritten = new(SymbolEqualityComparer.Default);
        private readonly HashSet<Location> _reported = new();

        public BlockAnalysis(OperationBlockAnalysisContext context, Symbols symbols, IOperation root)
        {
            _context = context;
            _symbols = symbols;
            _root = root;
        }

        public void Run()
        {
            var operations = _root.DescendantsAndSelf().ToList();
            if (!operations.Any(operation => IsCoreOperation(operation, out _)))
            {
                return;
            }

            foreach (var operation in operations)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
                switch (operation)
                {
                    case IInvocationOperation invocation when IsTaskCombinator(invocation):
                        AnalyzeCombinator(invocation);
                        break;
                    case IBlockOperation block:
                        AnalyzeTaskLocals(block);
                        break;
                }
            }
        }

        private bool IsTaskCombinator(IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            return method.IsStatic
                && method.Name is "WhenAll" or "WhenAny"
                && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, _symbols.Task);
        }

        // Arguments are evaluated in order, so every operation after the first on a context starts
        // while the earlier ones are pending.
        private void AnalyzeCombinator(IInvocationOperation combinator)
        {
            var firstByContext = new Dictionary<ISymbol, IInvocationOperation>(SymbolEqualityComparer.Default);
            foreach (var element in CombinatorElements(combinator))
            {
                if (IsCoreOperation(element, out var operation))
                {
                    if (!TryGetContextKey(operation, out var key, out var display))
                    {
                        continue;
                    }

                    if (firstByContext.TryGetValue(key, out var pending))
                    {
                        Report(operation, $"Invocation resolves to '{Display(operation)}', runs on context '{display}', and is passed to Task.{combinator.TargetMethod.Name} after '{pending.Syntax}' on the same context, so it starts while that operation is still pending.");
                    }
                    else
                    {
                        firstByContext.Add(key, operation);
                    }

                    continue;
                }

                AnalyzeProjection(element, combinator);
            }
        }

        private static IEnumerable<IOperation> CombinatorElements(IInvocationOperation combinator)
        {
            foreach (var argument in combinator.Arguments)
            {
                var value = EfQueryOperationAnalysis.Unwrap(argument.Value);
                switch (value)
                {
                    case IArrayCreationOperation { Initializer: { } initializer }:
                        foreach (var element in initializer.ElementValues)
                        {
                            yield return EfQueryOperationAnalysis.Unwrap(element);
                        }

                        break;
                    // Matched by kind value: ICollectionExpressionOperation is newer than the oldest
                    // Roslyn the analyzers load into. Its child operations are its elements. A
                    // params span argument is an implicit one, so the syntax can't identify it.
                    case { } collection when (int)collection.Kind == EfQueryOperationAnalysis.CollectionExpressionOperationKind:
                        foreach (var element in collection.ChildOperations)
                        {
                            yield return EfQueryOperationAnalysis.Unwrap(element);
                        }

                        break;
                    default:
                        yield return value;
                        break;
                }
            }
        }

        // Each selector invocation starts the operation on the captured context while the
        // operations started for earlier elements are still pending.
        private void AnalyzeProjection(IOperation element, IInvocationOperation combinator)
        {
            if (!TryGetSelect(element, out var selector))
            {
                return;
            }

            var span = selector.Syntax.Span;
            foreach (var operation in DescendantsOutsideFunctions(selector.Body))
            {
                if (IsCoreOperation(operation, out var efOperation)
                    && TryGetContextKey(efOperation, out var key, out var display)
                    && IsDeclaredOutside(key, span))
                {
                    Report(efOperation, $"Invocation resolves to '{Display(efOperation)}' and runs on context '{display}', which is captured from outside the selector of an Enumerable.Select passed to Task.{combinator.TargetMethod.Name}, so the operation started for each element begins while those started for earlier elements are still pending.");
                    return;
                }
            }
        }

        private bool TryGetSelect(IOperation operation, out IAnonymousFunctionOperation selector)
        {
            selector = null!;
            for (var hops = 0; hops < EfQueryLocals.MaxHops; hops++)
            {
                operation = EfQueryOperationAnalysis.Unwrap(operation);
                if (operation is ILocalReferenceOperation read && EfQueryLocals.TryResolve(read, out var value))
                {
                    operation = value;
                    continue;
                }

                if (operation is not IInvocationOperation invocation
                    || !SymbolEqualityComparer.Default.Equals(EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod).ContainingType.OriginalDefinition, _symbols.Enumerable))
                {
                    return false;
                }

                var name = invocation.TargetMethod.Name;
                if (name is "ToList" or "ToArray")
                {
                    var source = EfQueryOperationAnalysis.GetInvocationSource(invocation);
                    if (source is null)
                    {
                        return false;
                    }

                    operation = source;
                    continue;
                }

                var selectorArgument = invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 1);
                return name == "Select"
                    && selectorArgument is not null
                    && EfQueryOperationAnalysis.TryGetLambda(selectorArgument.Value, out selector);
            }

            return false;
        }

        private static bool IsDeclaredOutside(ISymbol key, Microsoft.CodeAnalysis.Text.TextSpan span) =>
            key is not (ILocalSymbol or IParameterSymbol)
            || key.DeclaringSyntaxReferences.All(reference => !span.Contains(reference.Span));

        // A task local whose operation is still unobserved: the next operation on the same context
        // in a later statement of the block starts while it may be pending. Any reference to the
        // local first could observe completion, so it ends the search without a finding.
        private void AnalyzeTaskLocals(IBlockOperation block)
        {
            var statements = block.Operations;
            for (var index = 0; index < statements.Length; index++)
            {
                if (statements[index] is not IVariableDeclarationGroupOperation group
                    || group.Declarations.Length != 1
                    || group.Declarations[0].Declarators.Length != 1
                    || group.Declarations[0].Declarators[0] is not { } declarator
                    || declarator.Symbol.IsRef
                    || declarator.Initializer?.Value is not { } initializer
                    || !IsCoreOperation(initializer, out var started)
                    || !TryGetContextKey(started, out var key, out var display))
                {
                    continue;
                }

                var local = declarator.Symbol;
                var events = statements.Skip(index + 1)
                    .SelectMany(DescendantsOutsideFunctions)
                    .Where(operation => operation is ILocalReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Local, local)
                        || IsCoreOperation(operation, out _))
                    .OrderBy(static operation => operation.Syntax.SpanStart);
                foreach (var operation in events)
                {
                    if (operation is ILocalReferenceOperation)
                    {
                        break;
                    }

                    var efOperation = (IInvocationOperation)operation;
                    if (TryGetContextKey(efOperation, out var otherKey, out _)
                        && SymbolEqualityComparer.Default.Equals(key, otherKey))
                    {
                        Report(efOperation, $"Invocation resolves to '{Display(efOperation)}', runs on context '{display}', and starts while task local '{local.Name}', initialized by '{started.Syntax}' on the same context, has not been awaited or otherwise referenced.");
                        break;
                    }
                }
            }
        }

        private bool IsCoreOperation(IOperation operation, out IInvocationOperation efOperation) =>
            EfAsyncOperations.TryGetCoreOperation(operation, _symbols.DbContext, _symbols.DbSet, _symbols.EfExtensions, out efOperation)
            && ReferenceEquals(EfQueryOperationAnalysis.Unwrap(operation), operation);

        private bool TryGetContextKey(IInvocationOperation operation, out ISymbol key, out string display)
        {
            key = null!;
            display = null!;
            var method = EfQueryOperationAnalysis.NormalizeMethod(operation.TargetMethod);
            IOperation? context;
            if (method.Name == "SaveChangesAsync" && EfAsyncOperations.IsDbContextMethod(method, _symbols.DbContext))
            {
                context = operation.Instance;
            }
            else if (method.Name == "FindAsync" && operation.Instance is not null)
            {
                context = ContextOfDbSet(operation.Instance);
            }
            else
            {
                var source = EfQueryOperationAnalysis.GetInvocationSource(operation);
                context = source is not null
                    && EfQueryOperationAnalysis.TryAnalyzeSource(source, _symbols.Queryable, _symbols.DbSet, _symbols.DbContext, _symbols.EfExtensions, out var analysis)
                        ? ContextOfDbSet(analysis.Origin)
                        : null;
            }

            if (context is null)
            {
                return false;
            }

            switch (EfQueryOperationAnalysis.Unwrap(context))
            {
                case ILocalReferenceOperation local when !local.Local.IsRef && IsNeverWritten(local.Local):
                    key = local.Local;
                    display = local.Local.Name;
                    return true;
                case IParameterReferenceOperation parameter when parameter.Parameter.RefKind == RefKind.None && IsNeverWritten(parameter.Parameter):
                    key = parameter.Parameter;
                    display = parameter.Parameter.Name;
                    return true;
                case IFieldReferenceOperation field when IsThisOrStatic(field.Instance):
                    key = field.Field;
                    display = MemberDisplay(field.Instance, field.Field);
                    return true;
                case IPropertyReferenceOperation property when IsThisOrStatic(property.Instance) && IsAutoProperty(property.Property):
                    key = property.Property;
                    display = MemberDisplay(property.Instance, property.Property);
                    return true;
                case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type: { } type }:
                    key = type;
                    display = "this";
                    return true;
                default:
                    return false;
            }
        }

        private IOperation? ContextOfDbSet(IOperation dbSetExpression)
        {
            switch (EfQueryOperationAnalysis.Unwrap(dbSetExpression))
            {
                case IPropertyReferenceOperation { Instance: { } instance } when EfQueryOperationAnalysis.IsOrDerivesFrom(instance.Type, _symbols.DbContext):
                    return instance;
                case IFieldReferenceOperation { Instance: { } instance } when EfQueryOperationAnalysis.IsOrDerivesFrom(instance.Type, _symbols.DbContext):
                    return instance;
                case IInvocationOperation { Instance: { } instance } invocation
                    when invocation.TargetMethod.Name == "Set" && EfQueryOperationAnalysis.IsOrDerivesFrom(instance.Type, _symbols.DbContext):
                    return instance;
                default:
                    return null;
            }
        }

        private bool IsNeverWritten(ISymbol symbol)
        {
            if (!_neverWritten.TryGetValue(symbol, out var result))
            {
                result = !_root.Descendants().Any(operation => operation switch
                {
                    ILocalReferenceOperation local => SymbolEqualityComparer.Default.Equals(local.Local, symbol) && IsWrite(local),
                    IParameterReferenceOperation parameter => SymbolEqualityComparer.Default.Equals(parameter.Parameter, symbol) && IsWrite(parameter),
                    _ => false,
                });
                _neverWritten.Add(symbol, result);
            }

            return result;
        }

        private static bool IsWrite(IOperation reference)
        {
            if (reference is ILocalReferenceOperation { IsDeclaration: true })
            {
                return true;
            }

            var current = reference;
            while (current.Parent is ITupleOperation or IConversionOperation)
            {
                current = current.Parent;
            }

            return current.Parent switch
            {
                IAssignmentOperation assignment => ReferenceEquals(assignment.Target, current),
                IIncrementOrDecrementOperation or IAddressOfOperation => true,
                IArgumentOperation { Parameter.RefKind: not RefKind.None and not RefKind.In and not RefKind.RefReadOnlyParameter } => true,
                _ => false,
            };
        }

        private static bool IsThisOrStatic(IOperation? instance) =>
            instance is null or IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance };

        private static bool IsAutoProperty(IPropertySymbol property) =>
            property.ContainingType.GetMembers()
                .OfType<IFieldSymbol>()
                .Any(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));

        private static string MemberDisplay(IOperation? instance, ISymbol member) =>
            instance is null ? $"{member.ContainingType.Name}.{member.Name}" : $"this.{member.Name}";

        private static string Display(IInvocationOperation operation) =>
            operation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        private void Report(IInvocationOperation operation, string evidence)
        {
            var location = operation.Syntax.GetLocation();
            if (!_reported.Add(location))
            {
                return;
            }

            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(DiagnosticPropertyNames.Confidence, Confidence)
                .Add(DiagnosticPropertyNames.Evidence, evidence)
                .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
                .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
                .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

            _context.ReportDiagnostic(EfDiagnostic.Create(Rule, location, properties));
        }

        private static IEnumerable<IOperation> DescendantsOutsideFunctions(IOperation operation)
        {
            var stack = new Stack<IOperation>();
            stack.Push(operation);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;
                foreach (var child in current.ChildOperations.Reverse())
                {
                    if (child is not (IAnonymousFunctionOperation or ILocalFunctionOperation))
                    {
                        stack.Push(child);
                    }
                }
            }
        }
    }

    private sealed class Symbols
    {
        public Symbols(
            INamedTypeSymbol task,
            INamedTypeSymbol enumerable,
            INamedTypeSymbol queryable,
            INamedTypeSymbol dbSet,
            INamedTypeSymbol dbContext,
            INamedTypeSymbol efExtensions)
        {
            Task = task;
            Enumerable = enumerable;
            Queryable = queryable;
            DbSet = dbSet;
            DbContext = dbContext;
            EfExtensions = efExtensions;
        }

        public INamedTypeSymbol Task { get; }

        public INamedTypeSymbol Enumerable { get; }

        public INamedTypeSymbol Queryable { get; }

        public INamedTypeSymbol DbSet { get; }

        public INamedTypeSymbol DbContext { get; }

        public INamedTypeSymbol EfExtensions { get; }
    }
}
