using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IncludeFilteredInMemoryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD024";
    public const string RuleTitle = "Included collection filtered in memory";
    public const string Confidence = "advisory";
    public const string DocumentationKey = "EFD024";
    public const string Impact = "Include loads every related row, and the method then keeps only a filtered subset in memory, so the query transfers, materializes, and (with tracking) tracks rows that are never used. The cost grows with the size of the collection.";
    public const string Remediation = "Filter in the Include, which EF Core 5 and later translate to SQL: Include(b => b.Posts.Where(p => p.Published)). With tracking, the navigation can still contain other entities of that type that the context already tracks; use AsNoTracking or a projection when that matters. Keep the full Include when the collection is small.";

    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "'{0}' is loaded in full by Include, but this method only uses it filtered; use a filtered Include";

    private static readonly ImmutableHashSet<string> SingleTerminals =
        ImmutableHashSet.Create(StringComparer.Ordinal, "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault");

    private static readonly ImmutableHashSet<string> FilteringOperators =
        ImmutableHashSet.Create(StringComparer.Ordinal, "Where", "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault", "Any", "All", "Count", "LongCount");

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Reports an Include of a collection navigation whose every use in the method, after the query is materialized, is filtered in memory with a predicate.",
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
                foreach (var declarator in root.Descendants().OfType<IVariableDeclaratorOperation>())
                {
                    if (declarator.Initializer?.Value is { } initializer
                        && TryGetLoad(initializer, enumerable, queryable, dbSet, dbContext, efExtensions, out var load, out var isList, out var includes))
                    {
                        foreach (var (include, navigation) in includes)
                        {
                            Analyze(blockContext, root, declarator.Symbol, isList, load, include, navigation, enumerable);
                        }
                    }
                }
            }
        });
    }

    private static void Analyze(
        OperationBlockAnalysisContext context,
        IOperation root,
        ILocalSymbol loaded,
        bool isList,
        IInvocationOperation load,
        IInvocationOperation include,
        IPropertySymbol navigation,
        INamedTypeSymbol enumerable)
    {
        var entityLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var entityParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
        if (!isList)
        {
            entityLocals.Add(loaded);
        }

        foreach (var operation in root.Descendants())
        {
            if (!isList)
            {
                break;
            }

            if (operation is IForEachLoopOperation loop
                && EfQueryOperationAnalysis.Unwrap(loop.Collection) is ILocalReferenceOperation collection
                && SymbolEqualityComparer.Default.Equals(collection.Local, loaded))
            {
                foreach (var local in loop.Locals)
                {
                    entityLocals.Add(local);
                }
            }
            else if (operation is IInvocationOperation call
                && IsEnumerableCallOn(call, loaded, enumerable))
            {
                foreach (var argument in call.Arguments)
                {
                    if (EfQueryOperationAnalysis.TryGetLambda(argument.Value, out var lambda) && lambda.Symbol.Parameters.Length >= 1)
                    {
                        entityParameters.Add(lambda.Symbol.Parameters[0]);
                    }
                }
            }
        }

        var filters = new List<IAnonymousFunctionOperation>();
        foreach (var operation in root.Descendants())
        {
            switch (operation)
            {
                case ILocalReferenceOperation reference when SymbolEqualityComparer.Default.Equals(reference.Local, loaded) && isList:
                    if (!(reference.Parent is IPropertyReferenceOperation || IsForEachCollection(reference) || IsEnumerableSource(reference, enumerable)))
                    {
                        return;
                    }

                    break;
                case ILocalReferenceOperation reference when entityLocals.Contains(reference.Local):
                    if (!CheckEntityUse(reference, navigation, enumerable, filters))
                    {
                        return;
                    }

                    break;
                case IParameterReferenceOperation reference when entityParameters.Contains(reference.Parameter):
                    if (!CheckEntityUse(reference, navigation, enumerable, filters))
                    {
                        return;
                    }

                    break;
            }
        }

        // Complementary filters, such as `SeriesId == null` and `SeriesId != null`, together keep
        // every row, which no filtered Include can narrow.
        if (filters.Count == 0 || HasComplementaryFilters(filters))
        {
            return;
        }

        var filteredUses = filters.Count;

        var evidence = $"'{include.Syntax}' loads every '{navigation.Name}' row for the entities in '{loaded.Name}', loaded by '{load.TargetMethod.Name}', and each of the {filteredUses} use(s) of '{navigation.Name}' in this method filters it in memory with a predicate.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, include.Syntax.GetLocation(), properties, navigation.Name));
    }

    // An entity variable may only be read through its properties or null-checked. A read of the
    // navigation must be the source of an Enumerable predicate operator.
    private static bool CheckEntityUse(IOperation reference, IPropertySymbol navigation, INamedTypeSymbol enumerable, List<IAnonymousFunctionOperation> filters)
    {
        if (IsNullCheck(reference))
        {
            return true;
        }

        if (reference.Parent is not IPropertyReferenceOperation property || !ReferenceEquals(property.Instance, reference))
        {
            return false;
        }

        if (!SymbolEqualityComparer.Default.Equals(property.Property.OriginalDefinition, navigation.OriginalDefinition))
        {
            return true;
        }

        IOperation current = property;
        while (current.Parent is IConversionOperation)
        {
            current = current.Parent;
        }

        if (current.Parent is IArgumentOperation { Parameter.Ordinal: 0, Parent: IInvocationOperation filter }
            && SymbolEqualityComparer.Default.Equals(EfQueryOperationAnalysis.NormalizeMethod(filter.TargetMethod).ContainingType.OriginalDefinition, enumerable)
            && FilteringOperators.Contains(filter.TargetMethod.Name)
            && filter.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 1) is { } predicate
            && EfQueryOperationAnalysis.TryGetLambda(predicate.Value, out var lambda))
        {
            filters.Add(lambda);
            return true;
        }

        return false;
    }

    private static bool TryGetLoad(
        IOperation initializer,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        out IInvocationOperation load,
        out bool isList,
        out List<(IInvocationOperation Include, IPropertySymbol Navigation)> includes)
    {
        load = null!;
        isList = false;
        includes = new List<(IInvocationOperation, IPropertySymbol)>();
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
        var name = method.Name;
        if (SymbolEqualityComparer.Default.Equals(containingType, efExtensions) && name.EndsWith("Async", StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - "Async".Length);
        }
        else if (!SymbolEqualityComparer.Default.Equals(containingType, queryable) && !SymbolEqualityComparer.Default.Equals(containingType, enumerable))
        {
            return false;
        }

        isList = name is "ToList" or "ToArray";
        if ((!isList && !SingleTerminals.Contains(name))
            || EfQueryOperationAnalysis.GetInvocationSource(invocation) is not { } source
            || !EfQueryOperationAnalysis.TryAnalyzeSource(source, queryable, dbSet, dbContext, efExtensions, out _))
        {
            return false;
        }

        var current = EfQueryOperationAnalysis.Unwrap(source);
        for (var hops = 0; hops < 64; hops++)
        {
            if (EfQueryLocals.TryFollow(current, 0, out _, out var localValue))
            {
                current = EfQueryOperationAnalysis.Unwrap(localValue);
                continue;
            }

            if (current is not IInvocationOperation step)
            {
                break;
            }

            var stepMethod = EfQueryOperationAnalysis.NormalizeMethod(step.TargetMethod);
            if (stepMethod.Name == "Include"
                && SymbolEqualityComparer.Default.Equals(stepMethod.ContainingType.OriginalDefinition, efExtensions)
                && step.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 1)?.Value is { } argumentValue
                && EfQueryOperationAnalysis.TryGetLambda(argumentValue, out var lambda)
                && lambda.Symbol.Parameters.Length == 1
                && EfQueryOperationAnalysis.GetLambdaExpression(lambda) is { } body
                && EfQueryOperationAnalysis.Unwrap(body) is IPropertyReferenceOperation { Instance: { } instance, Property: var navigation }
                && EfQueryOperationAnalysis.Unwrap(instance) is IParameterReferenceOperation parameter
                && SymbolEqualityComparer.Default.Equals(parameter.Parameter, lambda.Symbol.Parameters[0])
                && IsCollection(navigation.Type))
            {
                includes.Add((step, navigation));
            }

            if (EfQueryOperationAnalysis.GetInvocationSource(step) is not { } next)
            {
                break;
            }

            current = EfQueryOperationAnalysis.Unwrap(next);
        }

        load = invocation;
        return includes.Count > 0;
    }

    private static bool HasComplementaryFilters(List<IAnonymousFunctionOperation> filters)
    {
        var bodies = new HashSet<string>(StringComparer.Ordinal);
        var negations = new List<string>();
        foreach (var filter in filters)
        {
            if (filter.Syntax is not LambdaExpressionSyntax { ExpressionBody: { } body } || filter.Symbol.Parameters.Length != 1)
            {
                continue;
            }

            var parameter = filter.Symbol.Parameters[0].Name;
            bodies.Add(Canonical(body, parameter));
            if (Negation(body, parameter) is { } negation)
            {
                negations.Add(negation);
            }
        }

        return negations.Any(bodies.Contains);
    }

    // The predicate's tokens, with its parameter renamed, so `p => p.X == null` and
    // `r => r.X == null` compare equal.
    private static string Canonical(ExpressionSyntax expression, string parameter) =>
        string.Join(" ", StripParentheses(expression).DescendantTokens().Select(token =>
            token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText == parameter ? "$" : token.ValueText));

    // The canonical form of the predicate's logical negation, for `!e`, `==`/`!=`, and the
    // relational operators.
    private static string? Negation(ExpressionSyntax expression, string parameter)
    {
        switch (StripParentheses(expression))
        {
            case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.LogicalNotExpression):
                return Canonical(prefix.Operand, parameter);
            case BinaryExpressionSyntax binary when NegatedOperator(binary.Kind()) is { } negated:
                return $"{Canonical(binary.Left, parameter)} {negated} {Canonical(binary.Right, parameter)}";
            case var other:
                return $"! {Canonical(other, parameter)}";
        }
    }

    private static string? NegatedOperator(SyntaxKind kind) => kind switch
    {
        SyntaxKind.EqualsExpression => "!=",
        SyntaxKind.NotEqualsExpression => "==",
        SyntaxKind.LessThanExpression => ">=",
        SyntaxKind.GreaterThanOrEqualExpression => "<",
        SyntaxKind.GreaterThanExpression => "<=",
        SyntaxKind.LessThanOrEqualExpression => ">",
        _ => null,
    };

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool IsCollection(ITypeSymbol type) =>
        type.SpecialType != SpecialType.System_String
        && (type is IArrayTypeSymbol || type.AllInterfaces.Any(static candidate => candidate.SpecialType == SpecialType.System_Collections_IEnumerable || candidate.ToDisplayString() == "System.Collections.IEnumerable"));

    private static bool IsEnumerableCallOn(IInvocationOperation call, ILocalSymbol local, INamedTypeSymbol enumerable) =>
        SymbolEqualityComparer.Default.Equals(EfQueryOperationAnalysis.NormalizeMethod(call.TargetMethod).ContainingType.OriginalDefinition, enumerable)
        && EfQueryOperationAnalysis.GetInvocationSource(call) is { } source
        && EfQueryOperationAnalysis.Unwrap(source) is ILocalReferenceOperation reference
        && SymbolEqualityComparer.Default.Equals(reference.Local, local);

    private static bool IsEnumerableSource(IOperation reference, INamedTypeSymbol enumerable)
    {
        var current = reference;
        while (current.Parent is IConversionOperation)
        {
            current = current.Parent;
        }

        return current.Parent is IArgumentOperation { Parameter.Ordinal: 0, Parent: IInvocationOperation call }
            && SymbolEqualityComparer.Default.Equals(EfQueryOperationAnalysis.NormalizeMethod(call.TargetMethod).ContainingType.OriginalDefinition, enumerable);
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
            IIsPatternOperation pattern => ReferenceEquals(pattern.Value, current),
            _ => false,
        };
    }
}
