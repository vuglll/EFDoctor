using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrderByReplacesOrderingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD029";
    public const string RuleTitle = "OrderBy discards an earlier ordering";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD029";
    public const string Impact = "EF Core translates only the last OrderBy of a query, so the earlier ordering is discarded and rows are sorted by the replacing key alone. Rows that tie on that key come back in an order the database does not guarantee, which can also make pages built on the sort repeat or omit rows.";
    public const string Remediation = "If both keys were meant, replace this OrderBy with ThenBy (or OrderByDescending with ThenByDescending) so it adds a secondary sort. If replacing the earlier ordering was intended, delete the earlier ordering so the query says what it does.";

    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string RelationalExtensionsMetadataName = "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    private const string Message = "This {0} discards the earlier ordering of the same EF Core query; use ThenBy to add a secondary sort";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Correctness",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports an OrderBy or OrderByDescending over a proven EF Core query that is already ordered in the same inline chain, reached only through filtering, include, tracking, tagging, or split-query operators, so the earlier ordering is discarded.",
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

        var relationalExtensions = context.Compilation.GetTypeByMetadataName(RelationalExtensionsMetadataName);
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, queryable, dbSet, dbContext, efExtensions, relationalExtensions),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol? relationalExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        if (!IsPrimaryOrdering(method, queryable))
        {
            return;
        }

        // Walk back inline to the nearest earlier OrderBy. Only operators that cannot give the
        // earlier ordering a purpose may sit in between; anything else, including a local, ends
        // the walk without a finding. Paging, Distinct, and projections are deliberately absent.
        var discarded = new List<string>();
        IInvocationOperation? earlierOrderBy = null;
        var current = EfQueryOperationAnalysis.GetInvocationSource(invocation);
        while (current is not null && earlierOrderBy is null)
        {
            if (EfQueryOperationAnalysis.Unwrap(current) is not IInvocationOperation previous)
            {
                return;
            }

            var previousMethod = EfQueryOperationAnalysis.NormalizeMethod(previous.TargetMethod);
            if (IsPrimaryOrdering(previousMethod, queryable))
            {
                discarded.Add(previousMethod.Name);
                earlierOrderBy = previous;
            }
            else if (IsQueryableMethod(previousMethod, queryable) && previousMethod.Name is "ThenBy" or "ThenByDescending")
            {
                discarded.Add(previousMethod.Name);
            }
            else if (!IsPassThrough(previousMethod, queryable, efExtensions, relationalExtensions))
            {
                return;
            }

            current = EfQueryOperationAnalysis.GetInvocationSource(previous);
        }

        var earlierSource = earlierOrderBy is null ? null : EfQueryOperationAnalysis.GetInvocationSource(earlierOrderBy);
        if (earlierSource is null
            || !EfQueryOperationAnalysis.TryAnalyzeSource(earlierSource, queryable, dbSet, dbContext, efExtensions, out var analysis))
        {
            return;
        }

        discarded.Reverse();
        var methodDisplay = invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var evidence = $"Invocation resolves to '{methodDisplay}', its source is traced to DbSet origin '{analysis.Origin.Syntax}', and the same inline query chain is already ordered by '{string.Join(" -> ", discarded)}', which this {method.Name} replaces.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, GetLocation(invocation), properties, method.Name));
    }

    private static bool IsQueryableMethod(IMethodSymbol method, INamedTypeSymbol queryable) =>
        SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, queryable);

    private static bool IsPrimaryOrdering(IMethodSymbol method, INamedTypeSymbol queryable) =>
        method.Name is "OrderBy" or "OrderByDescending" && IsQueryableMethod(method, queryable);

    private static bool IsPassThrough(
        IMethodSymbol method,
        INamedTypeSymbol queryable,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol? relationalExtensions)
    {
        var containingType = method.ContainingType.OriginalDefinition;
        return (SymbolEqualityComparer.Default.Equals(containingType, queryable) && method.Name == "Where")
            || (SymbolEqualityComparer.Default.Equals(containingType, efExtensions)
                && EfQueryOperationAnalysis.ElementPreservingEfMethods.Contains(method.Name))
            || (relationalExtensions is not null
                && SymbolEqualityComparer.Default.Equals(containingType, relationalExtensions)
                && EfQueryOperationAnalysis.ElementPreservingRelationalMethods.Contains(method.Name));
    }

    // Reduced syntax is reported from the OrderBy name to the end of the invocation, so the
    // ordered receiver is excluded; static syntax has no such anchor and reports the invocation.
    private static Location GetLocation(IInvocationOperation invocation)
    {
        var syntax = invocation.Syntax;
        var isStaticForm = invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 0)?.Syntax is ArgumentSyntax;
        if (isStaticForm || syntax is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess })
        {
            return syntax.GetLocation();
        }

        return Location.Create(syntax.SyntaxTree, TextSpan.FromBounds(memberAccess.Name.SpanStart, syntax.Span.End));
    }
}
