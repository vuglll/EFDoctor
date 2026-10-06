using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LeadingWildcardSearchAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD023";
    public const string RuleTitle = "Leading-wildcard search on a column cannot seek an index";
    public const string Confidence = "advisory";
    public const string DocumentationKey = "EFD023";
    public const string Impact = "A search with a leading wildcard cannot seek an ordinary index on the column, so the database typically scans every row and the cost grows with table size. This is often acceptable for small or rarely searched tables.";

    private const string DbFunctionsExtensionsMetadataName = "Microsoft.EntityFrameworkCore.DbFunctionsExtensions";
    private const string Message = "This EF Core predicate searches a column with a leading wildcard, which cannot seek an ordinary index";
    private const string SqlServerLead = "For word or phrase search on SQL Server, use a full-text index with EF.Functions.Contains or EF.Functions.FreeText.";
    private const string NpgsqlLead = "On PostgreSQL, a pg_trgm GIN trigram index lets LIKE and ILIKE with leading wildcards use an index; for word or phrase search, use tsvector full-text search.";
    private const string GenericLead = "Consider full-text search (EF.Functions.Contains or EF.Functions.FreeText on SQL Server, tsvector on PostgreSQL) or a trigram index (pg_trgm on PostgreSQL).";
    private const string RemediationTail = " If a prefix search meets the need, use StartsWith, which can seek an index. For suffix searches, store a reversed or computed column and search it by prefix. If a scan is acceptable for this table or a supporting index already exists, suppress EFD023 with a recorded reason.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Reports Contains, EndsWith, or a leading-wildcard EF.Functions.Like on a mapped string column in a predicate of a proven EF Core query.",
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

        var scan = EfPredicateScan.Create(context.Compilation);
        if (scan is null)
        {
            return;
        }

        var dbFunctionsExtensions = context.Compilation.GetTypeByMetadataName(DbFunctionsExtensionsMetadataName);
        var provider = EfProviderDetection.Detect(context.Compilation).EligibleProvider;
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, scan, dbFunctionsExtensions, provider),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        EfPredicateScan scan,
        INamedTypeSymbol? dbFunctionsExtensions,
        EfProviderDetection.ProviderInfo? provider)
    {
        if (!scan.TryGetPredicate((IInvocationOperation)context.Operation, out var predicate))
        {
            return;
        }

        foreach (var operation in predicate.Operations(context.CancellationToken))
        {
            if (operation is not IInvocationOperation search
                || !TryMatchSearch(search, predicate.Parameter, dbFunctionsExtensions, out var match))
            {
                continue;
            }

            var transformation = match.Transformation is null ? string.Empty : $" after {match.Transformation}()";
            var evidence = $"The query is traced to DbSet origin '{predicate.Origin.Syntax}'. The predicate of {predicate.OperatorName} searches column '{match.Path}'{transformation} with {match.Form}: {match.Reason}, so the database cannot seek an ordinary index on the column.";
            if (provider is not null)
            {
                evidence += $" Referenced EF Core provider: {provider.Name}.";
            }

            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(DiagnosticPropertyNames.Confidence, Confidence)
                .Add(DiagnosticPropertyNames.Evidence, evidence)
                .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
                .Add(DiagnosticPropertyNames.SuggestedRemediation, GetRemediation(provider))
                .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

            context.ReportDiagnostic(EfDiagnostic.Create(Rule, search.Syntax.GetLocation(), properties));
        }
    }

    internal static string GetRemediation(EfProviderDetection.ProviderInfo? provider)
    {
        var lead = provider?.Identity switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => SqlServerLead,
            "Npgsql.EntityFrameworkCore.PostgreSQL" => NpgsqlLead,
            _ => GenericLead,
        };
        return lead + RemediationTail;
    }

    private static bool TryMatchSearch(
        IInvocationOperation invocation,
        IParameterSymbol parameter,
        INamedTypeSymbol? dbFunctionsExtensions,
        out SearchMatch match)
    {
        match = null!;
        var method = invocation.TargetMethod;

        // Only the single-string overloads: StringComparison overloads belong to EFD022, and
        // a collection's Contains(column) is an IN list rather than a string search.
        if (method.ContainingType.SpecialType == SpecialType.System_String
            && !method.IsStatic
            && method.Name is "Contains" or "EndsWith"
            && method.Parameters.Length == 1
            && method.Parameters[0].Type.SpecialType == SpecialType.System_String
            && invocation.Arguments.Length == 1
            && TryGetColumn(invocation.Instance, parameter, out var path, out var transformation)
            && !EfPredicateScan.ReferencesParameter(invocation.Arguments[0].Value, parameter))
        {
            var reason = method.Name == "Contains"
                ? "Contains matches anywhere in the value, which translates to a pattern with a leading wildcard"
                : "EndsWith matches a suffix, which translates to a pattern with a leading wildcard";
            match = new SearchMatch(path, transformation, method.Name, reason);
            return true;
        }

        var normalized = EfQueryOperationAnalysis.NormalizeMethod(method);
        if (dbFunctionsExtensions is null
            || normalized.Name != "Like"
            || !SymbolEqualityComparer.Default.Equals(normalized.ContainingType.OriginalDefinition, dbFunctionsExtensions)
            || normalized.Parameters.Length is not (3 or 4))
        {
            return false;
        }

        var column = GetArgument(invocation, 1);
        var pattern = GetArgument(invocation, 2);
        var leading = pattern is null ? null : GetLeadingCharacter(pattern);
        if (pattern is null
            || leading is not ('%' or '_')
            || !TryGetColumn(column, parameter, out path, out transformation)
            || EfPredicateScan.ReferencesParameter(pattern, parameter))
        {
            return false;
        }

        match = new SearchMatch(path, transformation, "EF.Functions.Like", $"the LIKE pattern starts with '{leading}'");
        return true;
    }

    private static IOperation? GetArgument(IInvocationOperation invocation, int ordinal) =>
        invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == ordinal)?.Value;

    // The column may be searched directly or through ToLower()/ToUpper(); EFD009 reports
    // case transformations only in equality and prefix comparisons, so this rule owns these.
    private static bool TryGetColumn(IOperation? operation, IParameterSymbol parameter, out string path, out string? transformation)
    {
        transformation = null;
        if (operation is not null
            && EfQueryOperationAnalysis.Unwrap(operation) is IInvocationOperation caseCall
            && caseCall.TargetMethod.ContainingType.SpecialType == SpecialType.System_String
            && !caseCall.TargetMethod.IsStatic
            && caseCall.TargetMethod.Parameters.Length == 0
            && caseCall.TargetMethod.Name is "ToLower" or "ToUpper")
        {
            transformation = caseCall.TargetMethod.Name;
            operation = caseCall.Instance;
        }

        return EfPredicateScan.TryGetColumnPath(operation, parameter, out path);
    }

    // Only patterns whose first character is provable: a constant, the leftmost operand of a
    // concatenation, or leading literal text of an interpolated string.
    private static char? GetLeadingCharacter(IOperation operation)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        if (operation.ConstantValue is { HasValue: true, Value: string text })
        {
            return text.Length > 0 ? text[0] : null;
        }

        return operation switch
        {
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } concatenation
                when concatenation.Type?.SpecialType == SpecialType.System_String =>
                GetLeadingCharacter(concatenation.LeftOperand),
            IInterpolatedStringOperation interpolated
                when interpolated.Parts.FirstOrDefault() is IInterpolatedStringTextOperation { Text.ConstantValue: { HasValue: true, Value: string first } }
                    && first.Length > 0 =>
                first[0],
            _ => null,
        };
    }

    private sealed class SearchMatch
    {
        public SearchMatch(string path, string? transformation, string form, string reason)
        {
            Path = path;
            Transformation = transformation;
            Form = form;
            Reason = reason;
        }

        public string Path { get; }

        public string? Transformation { get; }

        public string Form { get; }

        public string Reason { get; }
    }
}
