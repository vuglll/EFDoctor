using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantIncludeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD025";
    public const string RuleTitle = "Include path is redundant with another Include in the same query";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD025";
    public const string Impact = "EF Core merges every Include path in a query and loads each navigation along a path, so this include chain changes neither the generated SQL nor the loaded data. The cost is misleading query intent: readers may believe the loading shape is more complex than it is, and the redundant call is easy to leave behind when the other path changes.";
    public const string Remediation = "Delete the redundant Include/ThenInclude chain; the query still loads the same navigations through the other path. A repeated leading Include that branches into a different ThenInclude is required syntax and is not reported. If the chain is kept deliberately, for example to document intent, suppress EFD025 with a recorded justification.";

    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string RelationalExtensionsMetadataName = "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    private const string Message = "This Include chain is redundant because the same query already loads its navigation path";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Maintainability",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Reports Include/ThenInclude chains in a proven EF Core query that duplicate another chain's path or are a strict prefix of a longer path in the same query.");

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
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (queryable is null || dbSet is null || dbContext is null || efExtensions is null)
        {
            return;
        }

        var symbols = new QuerySymbols(
            queryable,
            dbSet,
            dbContext,
            efExtensions,
            context.Compilation.GetTypeByMetadataName(RelationalExtensionsMetadataName));
        context.RegisterOperationAction(operationContext => AnalyzeInvocation(operationContext, symbols), OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, QuerySymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsSupportedComposition(invocation.TargetMethod, symbols)
            || IsSourceOfSupportedComposition(invocation, symbols))
        {
            return;
        }

        var chains = new List<IncludeChain>();
        if (!TryAnalyzeChain(invocation, symbols, chains, out var origin) || chains.Count < 2)
        {
            return;
        }

        // A chain outside this expression came through a followed local, and the chain ending at the
        // local's value already judged it against the other chains there. It is reported here only
        // when a chain written here is what makes it redundant. A chain whose Include came through
        // the local but whose ThenInclude is written here straddles the two; its shorter form was
        // judged at the local's value, so every chain from the local is then left alone.
        var span = invocation.Syntax.Span;
        bool IsWrittenHere(IInvocationOperation operation) => span.Contains(operation.Syntax.Span);
        var straddles = chains.Any(chain => !IsWrittenHere(chain.First) && IsWrittenHere(chain.Last));
        var localChains = chains.Where(chain => !IsWrittenHere(chain.Last)).ToList();

        for (var index = 0; index < chains.Count; index++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var chain = chains[index];
            if (!chain.IsUsable)
            {
                continue;
            }

            var fromLocal = !IsWrittenHere(chain.First);
            if (fromLocal
                && (straddles
                    || localChains.Any(other => !ReferenceEquals(other, chain) && chain.IsStrictPrefixOf(other))
                    || localChains.TakeWhile(other => !ReferenceEquals(other, chain)).Any(chain.HasSamePath)))
            {
                continue;
            }

            string evidence;
            var covering = chains.FirstOrDefault(other => !ReferenceEquals(other, chain)
                && (!fromLocal || IsWrittenHere(other.Last))
                && chain.IsStrictPrefixOf(other));
            if (fromLocal && covering is null)
            {
                continue;
            }

            if (covering is not null)
            {
                evidence = $"The query is traced to DbSet origin '{origin.Syntax}'. Include path {chain.Path} is covered by a longer path: {covering.Path} in the same inline query chain already loads every navigation along it.";
            }
            else
            {
                var earlier = chains.Take(index).FirstOrDefault(chain.HasSamePath);
                if (earlier is null)
                {
                    continue;
                }

                evidence = $"The query is traced to DbSet origin '{origin.Syntax}'. Include path {chain.Path} is a duplicate: the earlier include chain in the same inline query chain already selects {earlier.Path}.";
            }

            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(DiagnosticPropertyNames.Confidence, Confidence)
                .Add(DiagnosticPropertyNames.Evidence, evidence)
                .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
                .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
                .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

            context.ReportDiagnostic(Diagnostic.Create(Rule, GetLocation(chain), properties));
        }
    }

    private static bool TryAnalyzeChain(
        IOperation operation,
        QuerySymbols symbols,
        List<IncludeChain> chains,
        out IOperation origin,
        int localHops = 0)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        origin = operation;
        if (EfQueryOperationAnalysis.IsOrDerivesFrom(operation.Type, symbols.DbSet))
        {
            return true;
        }

        // A query local carries its include chains when its value at this read is statically determined.
        if (EfQueryLocals.TryFollow(operation, localHops, out _, out var localValue))
        {
            return TryAnalyzeChain(localValue, symbols, chains, out origin, localHops + 1);
        }

        if (operation is not IInvocationOperation invocation)
        {
            return false;
        }

        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        if (method.Name == "Set" && EfQueryOperationAnalysis.IsOrDerivesFrom(method.ContainingType, symbols.DbContext))
        {
            return true;
        }

        var source = EfQueryOperationAnalysis.GetInvocationSource(invocation);
        if (!IsSupportedComposition(method, symbols)
            || source is null
            || !TryAnalyzeChain(source, symbols, chains, out origin, localHops))
        {
            origin = operation;
            return false;
        }

        // Chains are recorded while the recursion unwinds, so the list is in source order.
        if (!SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, symbols.EfExtensions))
        {
            return true;
        }

        if (method.Name == "Include")
        {
            chains.Add(CreateChain(invocation));
        }
        else if (method.Name == "ThenInclude" && chains.Count > 0)
        {
            chains[chains.Count - 1] = chains[chains.Count - 1].Extend(invocation);
        }

        return true;
    }

    private static IncludeChain CreateChain(IInvocationOperation invocation)
    {
        var pathArgument = invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 1);
        if (pathArgument?.Parameter?.Type.SpecialType == SpecialType.System_String)
        {
            // String paths are compared only with other string paths, and only when constant.
            if (pathArgument.Value.ConstantValue is { HasValue: true, Value: string text })
            {
                var names = text.Split('.');
                if (names.All(static name => name.Length > 0))
                {
                    return IncludeChain.ForString(invocation, names.ToImmutableArray());
                }
            }

            return IncludeChain.Unusable(invocation);
        }

        return EfIncludePaths.TryGetSelector(invocation, out var lambda)
            && EfIncludePaths.TryGetSegments(lambda, out var segments, out var isFiltered)
            && !isFiltered
                ? IncludeChain.ForExpression(invocation, lambda.Symbol.Parameters[0].Type, segments)
                : IncludeChain.Unusable(invocation);
    }

    private static bool IsSupportedComposition(IMethodSymbol method, QuerySymbols symbols)
    {
        method = EfQueryOperationAnalysis.NormalizeMethod(method);
        var containingType = method.ContainingType.OriginalDefinition;
        return (SymbolEqualityComparer.Default.Equals(containingType, symbols.Queryable)
                && EfQueryOperationAnalysis.ElementPreservingQueryableMethods.Contains(method.Name))
            || (SymbolEqualityComparer.Default.Equals(containingType, symbols.EfExtensions)
                && EfQueryOperationAnalysis.ElementPreservingEfMethods.Contains(method.Name))
            || (symbols.RelationalExtensions is not null
                && SymbolEqualityComparer.Default.Equals(containingType, symbols.RelationalExtensions)
                && EfQueryOperationAnalysis.ElementPreservingRelationalMethods.Contains(method.Name));
    }

    private static bool IsSourceOfSupportedComposition(IInvocationOperation invocation, QuerySymbols symbols)
    {
        IOperation current = invocation;
        while (current.Parent is IConversionOperation
            or IParenthesizedOperation
            or IArgumentOperation)
        {
            current = current.Parent;
        }

        if (current.Parent is not IInvocationOperation parent
            || !IsSupportedComposition(parent.TargetMethod, symbols))
        {
            return false;
        }

        var source = EfQueryOperationAnalysis.GetInvocationSource(parent);
        return source is not null
            && ReferenceEquals(EfQueryOperationAnalysis.Unwrap(source), invocation);
    }

    // Reduced syntax is reported from the Include name through the chain's last ThenInclude, so
    // the query receiver is excluded; static extension syntax has no such anchor and reports the
    // chain's outermost invocation.
    private static Location GetLocation(IncludeChain chain)
    {
        var last = chain.Last.Syntax;
        if (IsStaticForm(chain.Last))
        {
            return last.GetLocation();
        }

        var start = !IsStaticForm(chain.First)
            && chain.First.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
                ? memberAccess.Name.SpanStart
                : chain.First.Syntax.SpanStart;
        return Location.Create(last.SyntaxTree, TextSpan.FromBounds(start, last.Span.End));
    }

    private static bool IsStaticForm(IInvocationOperation invocation) =>
        invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 0)?.Syntax is ArgumentSyntax;

    private sealed class QuerySymbols
    {
        public QuerySymbols(
            INamedTypeSymbol queryable,
            INamedTypeSymbol dbSet,
            INamedTypeSymbol dbContext,
            INamedTypeSymbol efExtensions,
            INamedTypeSymbol? relationalExtensions)
        {
            Queryable = queryable;
            DbSet = dbSet;
            DbContext = dbContext;
            EfExtensions = efExtensions;
            RelationalExtensions = relationalExtensions;
        }

        public INamedTypeSymbol Queryable { get; }

        public INamedTypeSymbol DbSet { get; }

        public INamedTypeSymbol DbContext { get; }

        public INamedTypeSymbol EfExtensions { get; }

        public INamedTypeSymbol? RelationalExtensions { get; }
    }

    private enum IncludeChainKind
    {
        Unusable,
        Expression,
        String,
    }

    private sealed class IncludeChain
    {
        private IncludeChain(
            IInvocationOperation first,
            IInvocationOperation last,
            IncludeChainKind kind,
            ITypeSymbol? root,
            ImmutableArray<IPropertySymbol> properties,
            ImmutableArray<string> names)
        {
            First = first;
            Last = last;
            Kind = kind;
            Root = root;
            Properties = properties;
            Names = names;
        }

        public IInvocationOperation First { get; }

        public IInvocationOperation Last { get; }

        public string Path => Kind == IncludeChainKind.String
            ? $"\"{string.Join(".", Names)}\""
            : $"'{Root?.Name}.{string.Join(".", Properties.Select(static segment => segment.Name))}'";

        public bool IsUsable => Kind != IncludeChainKind.Unusable;

        private IncludeChainKind Kind { get; }

        private ITypeSymbol? Root { get; }

        private ImmutableArray<IPropertySymbol> Properties { get; }

        private ImmutableArray<string> Names { get; }

        private int Length => Kind == IncludeChainKind.String ? Names.Length : Properties.Length;

        public static IncludeChain ForExpression(IInvocationOperation invocation, ITypeSymbol root, ImmutableArray<IPropertySymbol> segments) =>
            new(invocation, invocation, IncludeChainKind.Expression, root, segments, ImmutableArray<string>.Empty);

        public static IncludeChain ForString(IInvocationOperation invocation, ImmutableArray<string> names) =>
            new(invocation, invocation, IncludeChainKind.String, null, ImmutableArray<IPropertySymbol>.Empty, names);

        public static IncludeChain Unusable(IInvocationOperation invocation) =>
            new(invocation, invocation, IncludeChainKind.Unusable, null, ImmutableArray<IPropertySymbol>.Empty, ImmutableArray<string>.Empty);

        // A ThenInclude that cannot be resolved to a plain, unfiltered property path makes the
        // whole chain unusable, and an unusable chain stays unusable.
        public IncludeChain Extend(IInvocationOperation thenInclude)
        {
            if (Kind != IncludeChainKind.Expression
                || !EfIncludePaths.TryGetSelector(thenInclude, out var lambda)
                || !EfIncludePaths.TryGetSegments(lambda, out var segments, out var isFiltered)
                || isFiltered)
            {
                return new(First, thenInclude, IncludeChainKind.Unusable, Root, Properties, Names);
            }

            return new(
                First,
                thenInclude,
                IncludeChainKind.Expression,
                Root,
                Properties.AddRange(segments),
                Names);
        }

        public bool HasSamePath(IncludeChain other) =>
            other.Length == Length && IsPrefixOf(other);

        public bool IsStrictPrefixOf(IncludeChain other) =>
            other.Length > Length && IsPrefixOf(other);

        private bool IsPrefixOf(IncludeChain other)
        {
            if (!IsUsable || other.Kind != Kind)
            {
                return false;
            }

            for (var index = 0; index < Length; index++)
            {
                var equal = Kind == IncludeChainKind.String
                    ? string.Equals(Names[index], other.Names[index], StringComparison.Ordinal)
                    : SymbolEqualityComparer.Default.Equals(Properties[index].OriginalDefinition, other.Properties[index].OriginalDefinition);
                if (!equal)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
