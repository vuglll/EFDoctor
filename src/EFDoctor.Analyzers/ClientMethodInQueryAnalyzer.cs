using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClientMethodInQueryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD028";
    public const string RuleTitle = "Client-only method in a SQL-translated query";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD028";
    public const string Impact = "EF Core cannot translate a method of the application to SQL. Because client evaluation of predicates and orderings is disabled since EF Core 3.0, the query throws an InvalidOperationException (\"could not be translated\") when it executes.";

    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "Method '{0}' cannot be translated to SQL inside this query's {1}";
    private const string Remediation = "Inline the method's logic as an expression EF Core can translate, map the method to a database function (DbFunction or HasDbFunction), or, only when the result is known to be small, materialize first and apply the method on the client. If a query-rewriting library translates the method, suppress with a recorded reason.";

    private static readonly ImmutableHashSet<string> QueryableOperators = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Where",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Any",
        "All",
        "Count",
        "LongCount",
        "First",
        "FirstOrDefault",
        "Single",
        "SingleOrDefault",
        "Last",
        "LastOrDefault");

    private static readonly ImmutableHashSet<string> EfAsyncOperators = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "AnyAsync",
        "AllAsync",
        "CountAsync",
        "LongCountAsync",
        "FirstAsync",
        "FirstOrDefaultAsync",
        "SingleAsync",
        "SingleOrDefaultAsync",
        "LastAsync",
        "LastOrDefaultAsync");

    // Attributes by which EF Core or a query-rewriting library translates a method:
    // EF Core, EntityFrameworkCore.Projectables, NeinLinq, DelegateDecompiler, and LinqKit.
    private static readonly ImmutableHashSet<string> TranslatedMethodAttributes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "DbFunctionAttribute",
        "ProjectableAttribute",
        "InjectLambdaAttribute",
        "ComputedAttribute",
        "ExpandableAttribute");

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Correctness",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports a method declared in the analyzed source that reads the query's lambda parameter inside a proven EF Core query predicate or ordering, which EF Core cannot translate to SQL.",
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

        var compilation = context.Compilation;
        var dbFunctionNames = new Lazy<ImmutableHashSet<string>>(
            () => CollectDbFunctionNames(compilation, context.CancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, queryable, dbSet, dbContext, efExtensions, dbFunctionNames),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        Lazy<ImmutableHashSet<string>> dbFunctionNames)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        method = method.OriginalDefinition;
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction)
            || !method.Locations.Any(static location => location.IsInSource)
            || FirstAncestor<IAnonymousFunctionOperation>(invocation) is null
            || IsTranslatedMethod(method, dbContext, dbFunctionNames))
        {
            return;
        }

        // Walk out through the enclosing lambdas to the first one consumed by a translated query
        // operator over a proven EF source. Lambdas consumed by other methods, such as an
        // Enumerable operator over a navigation, are part of the same expression and are skipped.
        var parameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
        for (var lambda = FirstAncestor<IAnonymousFunctionOperation>(invocation);
            lambda is not null;
            lambda = FirstAncestor<IAnonymousFunctionOperation>(lambda))
        {
            foreach (var parameter in lambda.Symbol.Parameters)
            {
                parameters.Add(parameter);
            }

            var consumer = ConsumingInvocation(lambda);
            if (consumer is null || !IsTranslatedOperator(consumer, queryable, efExtensions, out var operatorName))
            {
                continue;
            }

            var source = EfQueryOperationAnalysis.GetInvocationSource(consumer);
            if (source is null
                || !EfQueryOperationAnalysis.TryAnalyzeSource(source, queryable, dbSet, dbContext, efExtensions, out var analysis, followComposedLocals: true)
                || !ReadsParameter(invocation, parameters))
            {
                return;
            }

            Report(context, invocation, operatorName, analysis);
            return;
        }
    }

    private static void Report(OperationAnalysisContext context, IInvocationOperation invocation, string operatorName, EfQueryChainAnalysis analysis)
    {
        var methodDisplay = invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var evidence = $"Invocation resolves to '{methodDisplay}', which is declared in this project's source and reads the query's lambda parameter, inside the {operatorName} of a query traced to DbSet origin '{analysis.Origin.Syntax}'. EF Core has no SQL translation for it.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties, invocation.TargetMethod.Name, operatorName));
    }

    // The invocation that receives `lambda` as an argument, directly or through the implicit
    // conversion of a lambda to an expression tree or delegate.
    private static IInvocationOperation? ConsumingInvocation(IAnonymousFunctionOperation lambda)
    {
        IOperation current = lambda;
        while (current.Parent is IConversionOperation or IDelegateCreationOperation or IParenthesizedOperation)
        {
            current = current.Parent;
        }

        return current.Parent is IArgumentOperation { Parent: IInvocationOperation invocation } ? invocation : null;
    }

    private static bool IsTranslatedOperator(IInvocationOperation invocation, INamedTypeSymbol queryable, INamedTypeSymbol efExtensions, out string operatorName)
    {
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        var containingType = method.ContainingType.OriginalDefinition;
        operatorName = method.Name;
        return (SymbolEqualityComparer.Default.Equals(containingType, queryable) && QueryableOperators.Contains(method.Name))
            || (SymbolEqualityComparer.Default.Equals(containingType, efExtensions) && EfAsyncOperators.Contains(method.Name));
    }

    private static bool ReadsParameter(IInvocationOperation invocation, HashSet<IParameterSymbol> parameters)
    {
        var operands = invocation.Arguments.Select(static argument => argument.Value);
        if (invocation.Instance is not null)
        {
            operands = operands.Prepend(invocation.Instance);
        }

        return operands
            .SelectMany(static operand => operand.DescendantsAndSelf())
            .Any(operation => operation is IParameterReferenceOperation reference && parameters.Contains(reference.Parameter));
    }

    private static bool IsTranslatedMethod(IMethodSymbol method, INamedTypeSymbol dbContext, Lazy<ImmutableHashSet<string>> dbFunctionNames)
    {
        if (method.GetAttributes().Any(static attribute => attribute.AttributeClass is { } type && TranslatedMethodAttributes.Contains(type.Name)))
        {
            return true;
        }

        if (method.IsStatic && EfQueryOperationAnalysis.IsOrDerivesFrom(method.ContainingType, dbContext))
        {
            return true;
        }

        if (IsThrowOnlyStub(method))
        {
            return true;
        }

        return dbFunctionNames.Value.Contains(method.Name);
    }

    private static bool IsThrowOnlyStub(IMethodSymbol method)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax();
            var (expressionBody, body) = syntax switch
            {
                MethodDeclarationSyntax declaration => (declaration.ExpressionBody, declaration.Body),
                LocalFunctionStatementSyntax local => (local.ExpressionBody, local.Body),
                _ => (null, null),
            };

            if (expressionBody?.Expression is ThrowExpressionSyntax
                || (body is { Statements.Count: 1 } && body.Statements[0] is ThrowStatementSyntax))
            {
                return true;
            }
        }

        return false;
    }

    // Identifier and string-literal tokens in the arguments of every `HasDbFunction` call, such as
    // `HasDbFunction(typeof(F).GetMethod(nameof(F.Distance)))` or `HasDbFunction(() => Distance(0, 0))`.
    private static ImmutableHashSet<string> CollectDbFunctionNames(Compilation compilation, CancellationToken cancellationToken)
    {
        var names = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot(cancellationToken);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    GenericNameSyntax generic => generic.Identifier.ValueText,
                    _ => null,
                };
                if (name != "HasDbFunction")
                {
                    continue;
                }

                foreach (var token in invocation.ArgumentList.DescendantTokens())
                {
                    if (token.IsKind(SyntaxKind.IdentifierToken) || token.IsKind(SyntaxKind.StringLiteralToken))
                    {
                        names.Add(token.ValueText);
                    }
                }
            }
        }

        return names.ToImmutable();
    }

    private static T? FirstAncestor<T>(IOperation operation)
        where T : class, IOperation
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
