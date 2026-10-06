using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonSargableCaseTransformAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD009";
    public const string RuleTitle = "Case transformation on a column prevents an index seek";
    public const string Confidence = "medium";
    public const string DocumentationKey = "EFD009";
    public const string Impact = "Wrapping the column in LOWER or UPPER prevents the database from using an ordinary index on that column for this predicate, typically forcing a scan whose cost grows with table size; actual impact depends on existing indexes, collation, and data volume.";

    private const string Message = "This EF Core predicate applies a case transformation to a column, which prevents an index seek on it";
    private const string SqlServerLead = "SQL Server's default collations are case-insensitive, so remove the transformation and compare the column directly. If the column uses a case-sensitive collation, consider a case-insensitive collation for it or an indexed computed column that stores the transformed value.";
    private const string NpgsqlLead = "PostgreSQL compares text case-sensitively by default, so keep lookups index-friendly with a case-insensitive column type or collation (citext or a nondeterministic collation), EF.Functions.ILike with a supporting index, or an expression index on lower(column).";
    private const string GenericLead = "If the column's collation is case-insensitive (the SQL Server default), remove the transformation and compare the column directly. Otherwise use a case-insensitive collation or column type, or an index on the transformed expression.";
    private const string RemediationTail = " Normalize the comparison value in C# rather than transforming the column. If the database already has an index on the transformed expression, such as LOWER(column), the predicate can still seek; suppress EFD009 with a recorded reason.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Reports ToLower() or ToUpper() applied to a mapped string column in a predicate of a proven EF Core query when the column is compared with a value that does not reference the entity.",
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

        var provider = EfProviderDetection.Detect(context.Compilation).EligibleProvider;
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, scan, provider),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        EfPredicateScan scan,
        EfProviderDetection.ProviderInfo? provider)
    {
        if (!scan.TryGetPredicate((IInvocationOperation)context.Operation, out var predicate))
        {
            return;
        }

        foreach (var operation in predicate.Operations(context.CancellationToken))
        {
            if (operation is not IInvocationOperation transformation
                || !IsCaseTransformation(transformation)
                || !EfPredicateScan.TryGetColumnPath(transformation.Instance, predicate.Parameter, out var path)
                || !TryGetComparison(transformation, predicate.Parameter, out var comparison))
            {
                continue;
            }

            var evidence = $"The query is traced to DbSet origin '{predicate.Origin.Syntax}'. The predicate of {predicate.OperatorName} applies {transformation.TargetMethod.Name}() to column '{path}' and compares the result using {comparison}, so the database evaluates the function for each candidate row instead of seeking an index on the column.";
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

            context.ReportDiagnostic(EfDiagnostic.Create(Rule, transformation.Syntax.GetLocation(), properties));
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

    private static bool IsCaseTransformation(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return method.ContainingType.SpecialType == SpecialType.System_String
            && !method.IsStatic
            && method.Parameters.Length == 0
            && method.Name is "ToLower" or "ToUpper"
            && invocation.Instance is not null;
    }

    private static bool TryGetComparison(IInvocationOperation transformation, IParameterSymbol parameter, out string comparison)
    {
        comparison = string.Empty;
        IOperation current = transformation;
        while (current.Parent is IConversionOperation { IsImplicit: true } or IParenthesizedOperation)
        {
            current = current.Parent;
        }

        IOperation? value = null;
        switch (current.Parent)
        {
            case IBinaryOperation binary
                when binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals:
                value = ReferenceEquals(binary.LeftOperand, current) ? binary.RightOperand : binary.LeftOperand;
                comparison = binary.OperatorKind == BinaryOperatorKind.Equals ? "==" : "!=";
                break;

            // The transformed column is the receiver: `column.ToLower().Equals(value)` or `.StartsWith(value)`.
            case IInvocationOperation receiverCall
                when ReferenceEquals(receiverCall.Instance, current)
                    && IsStringMethod(receiverCall.TargetMethod, "Equals", "StartsWith")
                    && receiverCall.Arguments.Length == 1:
                value = receiverCall.Arguments[0].Value;
                comparison = receiverCall.TargetMethod.Name;
                break;

            case IArgumentOperation { Parent: IInvocationOperation argumentCall }
                when argumentCall.TargetMethod.ContainingType.SpecialType == SpecialType.System_String
                    && argumentCall.TargetMethod.Name == "Equals":
                if (argumentCall.TargetMethod.IsStatic && HasStringParameters(argumentCall.TargetMethod, 2))
                {
                    value = argumentCall.Arguments.FirstOrDefault(argument => !ReferenceEquals(argument.Value, current))?.Value;
                    comparison = "string.Equals";
                }
                else if (!argumentCall.TargetMethod.IsStatic && HasStringParameters(argumentCall.TargetMethod, 1))
                {
                    value = argumentCall.Instance;
                    comparison = "Equals";
                }

                break;
        }

        return value is not null && !EfPredicateScan.ReferencesParameter(value, parameter);
    }

    // Only the overloads without a StringComparison or culture argument: those overloads
    // belong to EFD022 and are not recognized comparisons here.
    private static bool IsStringMethod(IMethodSymbol method, params string[] names) =>
        method.ContainingType.SpecialType == SpecialType.System_String
        && !method.IsStatic
        && names.Contains(method.Name)
        && HasStringParameters(method, 1);

    private static bool HasStringParameters(IMethodSymbol method, int count) =>
        method.Parameters.Length == count
        && method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_String);
}
