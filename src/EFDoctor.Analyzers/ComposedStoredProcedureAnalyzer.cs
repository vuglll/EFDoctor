using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ComposedStoredProcedureAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD031";
    public const string RuleTitle = "LINQ composed over a stored-procedure call";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD031";
    public const string Impact = "EF Core composes LINQ over raw SQL by wrapping it in a subquery, which it cannot do with a stored-procedure call. The query throws an InvalidOperationException (\"FromSqlRaw or FromSqlInterpolated was called with non-composable SQL and with a query composing over it\") when it executes.";
    public const string Remediation = "Materialize the procedure's result first with AsEnumerable() or ToList(), and compose on the client when the result is small, or replace the procedure with a composable table-valued function or view (SELECT * FROM dbo.Function(...)) so EF Core can add the composition in SQL.";

    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string RelationalExtensionsMetadataName = "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    private const string Message = "EF Core cannot compose {0} over the stored-procedure call in {1}";

    private static readonly Regex ProcedureCall = new(@"^\s*(EXEC|EXECUTE|CALL)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly ImmutableHashSet<string> FromSqlMethods =
        ImmutableHashSet.Create(StringComparer.Ordinal, "FromSqlRaw", "FromSql", "FromSqlInterpolated");

    private static readonly ImmutableHashSet<string> PassThroughMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "AsNoTracking",
            "AsNoTrackingWithIdentityResolution",
            "AsTracking",
            "IgnoreQueryFilters",
            "IgnoreAutoIncludes",
            "TagWith",
            "TagWithCallSite");

    // EF Core async terminals that enumerate the query as it is, without composing over it.
    private static readonly ImmutableHashSet<string> EnumeratingAsyncMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ToListAsync",
            "ToArrayAsync",
            "ToDictionaryAsync",
            "ToHashSetAsync",
            "ForEachAsync",
            "LoadAsync",
            "AsAsyncEnumerable");

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Correctness",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports a LINQ operator that EF Core would compose into SQL over a FromSqlRaw, FromSql, or FromSqlInterpolated call whose SQL begins with EXEC, EXECUTE, or CALL.",
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
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        var relationalExtensions = context.Compilation.GetTypeByMetadataName(RelationalExtensionsMetadataName);
        if (queryable is null || efExtensions is null || relationalExtensions is null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, queryable, efExtensions, relationalExtensions),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol queryable,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol relationalExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        if (!IsComposingOperator(method, queryable, efExtensions)
            || EfQueryOperationAnalysis.GetInvocationSource(invocation) is not { } source
            || !TryFindProcedureCall(source, efExtensions, relationalExtensions, out var fromSql, out var keyword))
        {
            return;
        }

        var operatorName = method.Name;
        var fromSqlName = fromSql.TargetMethod.Name;
        var evidence = $"'{operatorName}' composes over '{fromSqlName}', whose SQL begins with '{keyword}': a stored-procedure call, which EF Core cannot wrap in a subquery.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties, operatorName, fromSqlName));
    }

    private static bool IsComposingOperator(IMethodSymbol method, INamedTypeSymbol queryable, INamedTypeSymbol efExtensions)
    {
        var containingType = method.ContainingType.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(containingType, queryable))
        {
            return method.Name != "AsQueryable";
        }

        return SymbolEqualityComparer.Default.Equals(containingType, efExtensions)
            && (method.Name is "Include" or "ThenInclude"
                || (method.Name.EndsWith("Async", StringComparison.Ordinal) && !EnumeratingAsyncMethods.Contains(method.Name)));
    }

    private static bool TryFindProcedureCall(
        IOperation source,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol relationalExtensions,
        out IInvocationOperation fromSql,
        out string keyword)
    {
        fromSql = null!;
        keyword = null!;
        var current = EfQueryOperationAnalysis.Unwrap(source);
        for (var hops = 0; hops < 32; hops++)
        {
            if (EfQueryLocals.TryFollow(current, 0, out _, out var value))
            {
                current = EfQueryOperationAnalysis.Unwrap(value);
                continue;
            }

            if (current is not IInvocationOperation invocation)
            {
                return false;
            }

            var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
            var containingType = method.ContainingType.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(containingType, efExtensions) && PassThroughMethods.Contains(method.Name)
                && EfQueryOperationAnalysis.GetInvocationSource(invocation) is { } next)
            {
                current = EfQueryOperationAnalysis.Unwrap(next);
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(containingType, relationalExtensions) || !FromSqlMethods.Contains(method.Name))
            {
                return false;
            }

            var sql = invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Name == "sql")?.Value;
            if (sql is null || LeadingText(EfQueryOperationAnalysis.Unwrap(sql)) is not { } text)
            {
                return false;
            }

            var match = ProcedureCall.Match(text);
            if (!match.Success)
            {
                return false;
            }

            fromSql = invocation;
            keyword = match.Groups[1].Value;
            return true;
        }

        return false;
    }

    // The constant SQL, or the literal text an interpolated SQL string starts with.
    private static string? LeadingText(IOperation sql)
    {
        if (sql.ConstantValue is { HasValue: true, Value: string constant })
        {
            return constant;
        }

        if (sql is IInterpolatedStringOperation interpolated
            && interpolated.Parts.FirstOrDefault() is IInterpolatedStringTextOperation { Text.ConstantValue: { HasValue: true, Value: string text } })
        {
            return text;
        }

        return null;
    }
}
