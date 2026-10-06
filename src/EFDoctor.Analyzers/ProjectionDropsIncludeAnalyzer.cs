using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProjectionDropsIncludeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD017";
    public const string RuleTitle = "Include is ignored by a later non-entity projection";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD017";
    public const string Impact = "The Select projection determines the data EF Core loads for this query. Because the projected result contains no entity on which the preceding Include can take effect, the include does not populate navigation state in the result; it is ignored for this query and can mislead readers into expecting loaded navigations that are absent.";

    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string RelationalExtensionsMetadataName = "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    private const string Message = "This Include does not populate navigation data because the later Select projects only non-entity values";
    private const string GeneralRemediation = "If the projected values are all the caller needs, remove the redundant Include/ThenInclude and select any required navigation data explicitly in the projection; the projection itself requests every value it references.";
    private const string NavigationRemediation = "The projection already reads data through the included navigation, and the projection itself requests every value it references, so remove the redundant Include/ThenInclude and keep the explicit projection.";
    private const string EntityRemediation = " If the caller genuinely requires populated navigation state, return the entity (or an entity-bearing result) from the query instead of a scalar or DTO shape. Do not materialize the query before Select merely to honor the include, because that loads complete entities client-side.";
    private const int MaxClassificationDepth = 32;

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Correctness",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports proven EF Core query chains whose resolved Include or ThenInclude calls are followed by a Queryable.Select that projects only scalar, value, or newly constructed non-entity results.",
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
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        if (method.Name != "Select"
            || !SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, symbols.Queryable))
        {
            return;
        }

        var selectorArgument = invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 1)?.Value;
        var source = EfQueryOperationAnalysis.GetInvocationSource(invocation);
        if (selectorArgument is null
            || source is null
            || !EfQueryOperationAnalysis.TryGetLambda(selectorArgument, out var selector)
            || selector.Symbol.Parameters.Length != 1)
        {
            return;
        }

        var includes = new List<IncludePath>();
        if (!TryAnalyzeChain(source, symbols, includes, out var origin) || includes.Count == 0)
        {
            return;
        }

        var result = GetSelectorResult(selector);
        var parameter = selector.Symbol.Parameters[0];
        if (result is null || !IsDefinitelyNonEntity(result, parameter, depth: 0, context.CancellationToken))
        {
            return;
        }

        var paths = includes
            .Select(static include => include.Path)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var navigation = FindIncludedNavigationRead(selector, parameter, includes);
        var evidence = $"The query is traced to DbSet origin '{origin.Syntax}' and resolved EF Core include path(s) {string.Join(", ", paths)} precede a resolved Queryable.Select whose selector returns {DescribeShape(result)}, which contains no entity on which the include can take effect.";
        if (navigation is not null)
        {
            evidence += $" The selector already reads data through included navigation '{navigation}'.";
        }

        var remediation = (navigation is null ? GeneralRemediation : NavigationRemediation) + EntityRemediation;
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties));
    }

    private static bool TryAnalyzeChain(
        IOperation operation,
        QuerySymbols symbols,
        List<IncludePath> includes,
        out IOperation origin,
        int localHops = 0)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        origin = operation;
        if (EfQueryOperationAnalysis.IsOrDerivesFrom(operation.Type, symbols.DbSet))
        {
            return true;
        }

        // A query local carries its include state when its value at this read is statically determined.
        if (EfQueryLocals.TryFollow(operation, localHops, out _, out var localValue))
        {
            return TryAnalyzeChain(localValue, symbols, includes, out origin, localHops + 1);
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

        var containingType = method.ContainingType.OriginalDefinition;
        var isEf = SymbolEqualityComparer.Default.Equals(containingType, symbols.EfExtensions);
        var isSupported =
            (SymbolEqualityComparer.Default.Equals(containingType, symbols.Queryable) && EfQueryOperationAnalysis.ElementPreservingQueryableMethods.Contains(method.Name))
            || (isEf && EfQueryOperationAnalysis.ElementPreservingEfMethods.Contains(method.Name))
            || (symbols.RelationalExtensions is not null
                && SymbolEqualityComparer.Default.Equals(containingType, symbols.RelationalExtensions)
                && EfQueryOperationAnalysis.ElementPreservingRelationalMethods.Contains(method.Name));
        var source = EfQueryOperationAnalysis.GetInvocationSource(invocation);
        if (!isSupported || source is null || !TryAnalyzeChain(source, symbols, includes, out origin, localHops))
        {
            origin = operation;
            return false;
        }

        if (isEf && method.Name == "Include")
        {
            // String-based includes cannot be validated without the runtime EF model, so only
            // expression-based includes contribute paths.
            if (EfIncludePaths.TryGetSelector(invocation, out var lambda))
            {
                includes.Add(EfIncludePaths.TryGetSegments(lambda, out var segments, out _)
                    ? IncludePath.Create(lambda.Symbol.Parameters[0].Type, segments)
                    : IncludePath.Fallback($"Include({lambda.Syntax})"));
            }
        }
        else if (isEf && method.Name == "ThenInclude" && includes.Count > 0 && EfIncludePaths.TryGetSelector(invocation, out var lambda))
        {
            var previous = includes[includes.Count - 1];
            includes[includes.Count - 1] = EfIncludePaths.TryGetSegments(lambda, out var segments, out _)
                ? previous.Append(segments)
                : previous.AppendFallback($"ThenInclude({lambda.Syntax})");
        }

        return true;
    }

    private static IOperation? GetSelectorResult(IAnonymousFunctionOperation selector)
    {
        if (selector.Body is IBlockOperation block)
        {
            return block.Operations.Length == 1 && block.Operations[0] is IReturnOperation singleReturn
                ? singleReturn.ReturnedValue
                : null;
        }

        return selector.Body;
    }

    private static bool IsDefinitelyNonEntity(
        IOperation operation,
        IParameterSymbol parameter,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > MaxClassificationDepth)
        {
            return false;
        }

        operation = EfQueryOperationAnalysis.Unwrap(operation);
        switch (operation)
        {
            case IParameterReferenceOperation reference
                when SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter):
                return false;
            case ITupleOperation tuple:
                return tuple.Elements.All(element => IsDefinitelyNonEntity(element, parameter, depth + 1, cancellationToken));
            case IAnonymousObjectCreationOperation anonymous:
                return anonymous.Initializers.All(initializer =>
                    initializer is ISimpleAssignmentOperation assignment
                    && IsDefinitelyNonEntity(assignment.Value, parameter, depth + 1, cancellationToken));
            case IObjectCreationOperation creation:
                return creation.Arguments.All(argument => IsDefinitelyNonEntity(argument.Value, parameter, depth + 1, cancellationToken))
                    && (creation.Initializer is null
                        || creation.Initializer.Initializers.All(initializer =>
                            initializer is ISimpleAssignmentOperation assignment
                            && IsDefinitelyNonEntity(assignment.Value, parameter, depth + 1, cancellationToken)));
            case IConditionalOperation conditional:
                return conditional.WhenFalse is not null
                    && IsScalarShapedType(conditional.Type)
                    && IsDefinitelyNonEntity(conditional.WhenTrue, parameter, depth + 1, cancellationToken)
                    && IsDefinitelyNonEntity(conditional.WhenFalse, parameter, depth + 1, cancellationToken);
            case IInvalidOperation:
                return false;
        }

        // A constant (including a typed null) carries no entity instance.
        if (operation.ConstantValue.HasValue)
        {
            return true;
        }

        // A scalar-shaped result cannot hold an entity regardless of how it is computed, for
        // example `parent.Owner.Name` or `parent.Children.Count()`. Reference-typed values other
        // than string (navigations, collections, arbitrary objects) remain ambiguous without the
        // runtime EF model, so they reject the diagnostic.
        return IsScalarShapedType(operation.Type);
    }

    private static bool IsScalarShapedType(ITypeSymbol? type)
    {
        if (type is null || type.TypeKind is TypeKind.Error or TypeKind.TypeParameter or TypeKind.Dynamic)
        {
            return false;
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        if (!type.IsValueType)
        {
            return false;
        }

        // Generic value types such as Nullable<T>, ValueTuple<...>, or KeyValuePair<,> can embed
        // their type arguments, so each argument must itself be scalar-shaped.
        return type is not INamedTypeSymbol named
            || named.TypeArguments.All(IsScalarShapedType);
    }

    private static string DescribeShape(IOperation result)
    {
        result = EfQueryOperationAnalysis.Unwrap(result);
        var typeName = result.Type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "unknown";
        return result switch
        {
            ITupleOperation => $"a tuple '{typeName}' of scalar values",
            IAnonymousObjectCreationOperation anonymous =>
                $"an anonymous object with scalar members {string.Join(", ", anonymous.Initializers.OfType<ISimpleAssignmentOperation>().Select(static assignment => MemberName(assignment.Target)))}",
            IObjectCreationOperation => $"a new '{typeName}' object built from scalar values",
            _ => $"a scalar value of type '{typeName}'",
        };
    }

    private static string MemberName(IOperation target) =>
        target is IPropertyReferenceOperation property ? property.Property.Name : target.Syntax.ToString();

    private static string? FindIncludedNavigationRead(
        IAnonymousFunctionOperation selector,
        IParameterSymbol parameter,
        IReadOnlyList<IncludePath> includes)
    {
        foreach (var descendant in selector.Body.DescendantsAndSelf())
        {
            if (descendant is not IPropertyReferenceOperation reference
                || reference.Instance is null
                || EfQueryOperationAnalysis.Unwrap(reference.Instance) is not IParameterReferenceOperation root
                || !SymbolEqualityComparer.Default.Equals(root.Parameter, parameter))
            {
                continue;
            }

            var include = includes.FirstOrDefault(include =>
                include.FirstSegment is not null
                && SymbolEqualityComparer.Default.Equals(include.FirstSegment.OriginalDefinition, reference.Property.OriginalDefinition));
            if (include is not null)
            {
                return $"{include.Root!.Name}.{reference.Property.Name}";
            }
        }

        return null;
    }

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

    private sealed class IncludePath
    {
        private IncludePath(string path, ITypeSymbol? root, IPropertySymbol? firstSegment, bool isResolved)
        {
            Path = path;
            Root = root;
            FirstSegment = firstSegment;
            IsResolved = isResolved;
        }

        public string Path { get; }

        public ITypeSymbol? Root { get; }

        public IPropertySymbol? FirstSegment { get; }

        private bool IsResolved { get; }

        public static IncludePath Create(ITypeSymbol root, ImmutableArray<IPropertySymbol> segments) =>
            new($"{root.Name}.{JoinSegments(segments)}", root, segments[0], isResolved: true);

        // Keeps the include invocation text as evidence when the selector is not a plain
        // source-rooted property path, for example a cast to a derived type.
        public static IncludePath Fallback(string text) => new(text, null, null, isResolved: false);

        public IncludePath Append(ImmutableArray<IPropertySymbol> segments) =>
            IsResolved
                ? new($"{Path}.{JoinSegments(segments)}", Root, FirstSegment, isResolved: true)
                : AppendFallback(JoinSegments(segments));

        public IncludePath AppendFallback(string text) => new($"{Path} then {text}", Root, FirstSegment, isResolved: false);

        private static string JoinSegments(ImmutableArray<IPropertySymbol> segments) =>
            string.Join(".", segments.Select(static segment => segment.Name));
    }
}
