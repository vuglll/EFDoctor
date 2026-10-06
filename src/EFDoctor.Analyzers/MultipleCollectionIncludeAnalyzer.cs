using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MultipleCollectionIncludeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD006";
    public const string RuleTitle = "Multiple sibling collection includes may create a cartesian explosion";
    public const string Confidence = "medium";
    public const string DocumentationKey = "EFD006";
    public const string Impact = "A single query that joins multiple sibling collections may multiply result rows and duplicate principal data as collection cardinalities grow, increasing database work, transfer size, and materialization cost; actual impact depends on the data and generated SQL.";

    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string GenericEnumerableMetadataName = "System.Collections.Generic.IEnumerable`1";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string RelationalExtensionsMetadataName = "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    private const string Message = "This EF Core query may create a cartesian explosion from multiple sibling collection includes";
    private const string Remediation = "Review the generated SQL and expected collection cardinalities. Consider AsSplitQuery, a projection that selects only required data, or separate targeted queries. Split and separate queries add round trips and can observe inconsistent data unless the transaction isolation level provides the required consistency.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Reports proven EF Core query chains with multiple distinct root-level collection Include paths and no explicit AsSplitQuery or AsSingleQuery.",
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
        var enumerable = context.Compilation.GetTypeByMetadataName(EnumerableMetadataName);
        var genericEnumerable = context.Compilation.GetTypeByMetadataName(GenericEnumerableMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        var relationalExtensions = context.Compilation.GetTypeByMetadataName(RelationalExtensionsMetadataName);
        if (queryable is null
            || enumerable is null
            || genericEnumerable is null
            || dbSet is null
            || dbContext is null
            || efExtensions is null
            || relationalExtensions is null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(
                operationContext,
                queryable,
                enumerable,
                genericEnumerable,
                dbSet,
                dbContext,
                efExtensions,
                relationalExtensions),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol queryable,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol genericEnumerable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol relationalExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsSupportedComposition(invocation.TargetMethod, queryable, efExtensions, relationalExtensions)
            || IsSourceOfSupportedComposition(invocation, queryable, efExtensions, relationalExtensions))
        {
            return;
        }

        var includes = new List<CollectionInclude>();
        if (!TryAnalyzeChain(
                invocation,
                queryable,
                enumerable,
                genericEnumerable,
                dbSet,
                dbContext,
                efExtensions,
                relationalExtensions,
                includes,
                out var origin,
                out var hasExplicitSplittingMode)
            || hasExplicitSplittingMode)
        {
            return;
        }

        var distinctIncludes = new List<CollectionInclude>();
        foreach (var include in includes)
        {
            if (distinctIncludes.Any(existing => SymbolEqualityComparer.Default.Equals(existing.Property, include.Property)))
            {
                continue;
            }

            distinctIncludes.Add(include);
        }

        // An include outside this expression came through a followed local. Includes are in source
        // order, so the first one did too, and the chain ending at the local's value reports it.
        if (distinctIncludes.Count < 2 || !invocation.Syntax.Span.Contains(distinctIncludes[1].Invocation.Syntax.Span))
        {
            return;
        }

        var paths = string.Join(", ", distinctIncludes.Select(static include => include.Path));
        var evidence = $"The query is traced to DbSet origin '{origin.Syntax}' and resolved EF Core Include calls select distinct sibling collection paths: {paths}. No explicit AsSplitQuery or AsSingleQuery occurs in the inline chain.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, distinctIncludes[1].Invocation.Syntax.GetLocation(), properties));
    }

    private static bool TryAnalyzeChain(
        IOperation operation,
        INamedTypeSymbol queryable,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol genericEnumerable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol relationalExtensions,
        List<CollectionInclude> includes,
        out IOperation origin,
        out bool hasExplicitSplittingMode,
        int localHops = 0)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        if (EfQueryOperationAnalysis.IsOrDerivesFrom(operation.Type, dbSet))
        {
            origin = operation;
            hasExplicitSplittingMode = false;
            return true;
        }

        // A query local carries its includes and splitting mode when its value at this read is
        // statically determined.
        if (EfQueryLocals.TryFollow(operation, localHops, out _, out var localValue))
        {
            return TryAnalyzeChain(
                localValue,
                queryable,
                enumerable,
                genericEnumerable,
                dbSet,
                dbContext,
                efExtensions,
                relationalExtensions,
                includes,
                out origin,
                out hasExplicitSplittingMode,
                localHops + 1);
        }

        if (operation is not IInvocationOperation invocation)
        {
            origin = operation;
            hasExplicitSplittingMode = false;
            return false;
        }

        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        if (method.Name == "Set" && EfQueryOperationAnalysis.IsOrDerivesFrom(method.ContainingType, dbContext))
        {
            origin = invocation;
            hasExplicitSplittingMode = false;
            return true;
        }

        if (!IsSupportedComposition(method, queryable, efExtensions, relationalExtensions))
        {
            origin = operation;
            hasExplicitSplittingMode = false;
            return false;
        }

        var source = EfQueryOperationAnalysis.GetInvocationSource(invocation);
        if (source is null
            || !TryAnalyzeChain(
                source,
                queryable,
                enumerable,
                genericEnumerable,
                dbSet,
                dbContext,
                efExtensions,
                relationalExtensions,
                includes,
                out origin,
                out hasExplicitSplittingMode,
                localHops))
        {
            origin = operation;
            hasExplicitSplittingMode = false;
            return false;
        }

        var containingType = method.ContainingType.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(containingType, relationalExtensions))
        {
            // Either explicit mode is a deliberate choice; EF Core itself only warns about multiple
            // collection includes when no splitting behavior was chosen.
            hasExplicitSplittingMode |= method.Name is "AsSplitQuery" or "AsSingleQuery";
            return true;
        }

        if (method.Name == "Include"
            && SymbolEqualityComparer.Default.Equals(containingType, efExtensions)
            && TryGetCollectionInclude(invocation, enumerable, genericEnumerable, out var include))
        {
            includes.Add(include);
        }

        return true;
    }

    private static bool IsSupportedComposition(
        IMethodSymbol method,
        INamedTypeSymbol queryable,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol relationalExtensions)
    {
        method = EfQueryOperationAnalysis.NormalizeMethod(method);
        var containingType = method.ContainingType.OriginalDefinition;
        return SymbolEqualityComparer.Default.Equals(containingType, queryable)
            || (SymbolEqualityComparer.Default.Equals(containingType, efExtensions)
                && method.Name != "AsAsyncEnumerable")
            || SymbolEqualityComparer.Default.Equals(containingType, relationalExtensions);
    }

    private static bool IsSourceOfSupportedComposition(
        IInvocationOperation invocation,
        INamedTypeSymbol queryable,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol relationalExtensions)
    {
        IOperation current = invocation;
        while (current.Parent is IConversionOperation
            or IParenthesizedOperation
            or IArgumentOperation)
        {
            current = current.Parent;
        }

        if (current.Parent is not IInvocationOperation parent
            || !IsSupportedComposition(parent.TargetMethod, queryable, efExtensions, relationalExtensions))
        {
            return false;
        }

        var source = EfQueryOperationAnalysis.GetInvocationSource(parent);
        return source is not null
            && ReferenceEquals(EfQueryOperationAnalysis.Unwrap(source), invocation);
    }

    private static bool TryGetCollectionInclude(
        IInvocationOperation invocation,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol genericEnumerable,
        out CollectionInclude include)
    {
        foreach (var argument in invocation.Arguments)
        {
            var anonymousFunction = FindAnonymousFunction(argument.Value);
            if (anonymousFunction is null || anonymousFunction.Symbol.Parameters.Length != 1)
            {
                continue;
            }

            var parameter = anonymousFunction.Symbol.Parameters[0];
            foreach (var descendant in anonymousFunction.Body.DescendantsAndSelf())
            {
                if (descendant is not IPropertyReferenceOperation propertyReference
                    || !IsDirectlyRootedAt(propertyReference, parameter)
                    || !IsCollectionType(propertyReference.Type, enumerable, genericEnumerable))
                {
                    continue;
                }

                var property = propertyReference.Property.OriginalDefinition;
                include = new CollectionInclude(
                    invocation,
                    property,
                    $"{property.ContainingType.Name}.{property.Name}");
                return true;
            }
        }

        include = null!;
        return false;
    }

    private static IAnonymousFunctionOperation? FindAnonymousFunction(IOperation operation)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        if (operation is IDelegateCreationOperation delegateCreation)
        {
            operation = EfQueryOperationAnalysis.Unwrap(delegateCreation.Target);
        }

        return operation as IAnonymousFunctionOperation;
    }

    private static bool IsDirectlyRootedAt(IPropertyReferenceOperation propertyReference, IParameterSymbol parameter)
    {
        var instance = propertyReference.Instance;
        if (instance is null)
        {
            return false;
        }

        instance = EfQueryOperationAnalysis.Unwrap(instance);
        return instance is IParameterReferenceOperation parameterReference
            && SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter);
    }

    private static bool IsCollectionType(
        ITypeSymbol? type,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol genericEnumerable)
    {
        if (type is null || type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, genericEnumerable)
            || namedType.AllInterfaces.Any(interfaceType =>
                SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, genericEnumerable))
            || SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, enumerable);
    }

    private sealed class CollectionInclude
    {
        public CollectionInclude(IInvocationOperation invocation, IPropertySymbol property, string path)
        {
            Invocation = invocation;
            Property = property;
            Path = path;
        }

        public IInvocationOperation Invocation { get; }

        public IPropertySymbol Property { get; }

        public string Path { get; }
    }
}
