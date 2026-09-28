using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticDbContextFieldAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD021";
    public const string RuleTitle = "DbContext is held in a static field";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD021";
    public const string Impact = "A DbContext is not thread-safe and is designed to be short-lived. Holding one in a static (process-lifetime) field shares a single instance across threads and requests, which can cause concurrency exceptions, stale cached entities, and unbounded change-tracker growth.";

    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string Message = "This static field holds an EF Core DbContext, which is not thread-safe and lives for the lifetime of the process";
    private const string Remediation = "Resolve a DbContext per unit of work instead of a static field — for example a scoped dependency-injection lifetime, an injected DbContextFactory, or a using block. If this field is provably single-threaded and short-lived by construction, suppress with a recorded reason.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Reliability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports an EF Core DbContext held in a static field, which is not thread-safe and outlives the intended unit of work.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        if (dbContext is null)
        {
            return;
        }

        context.RegisterSymbolAction(symbolContext => AnalyzeField(symbolContext, dbContext), SymbolKind.Field);
    }

    private static void AnalyzeField(SymbolAnalysisContext context, INamedTypeSymbol dbContext)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (!field.IsStatic
            || field.IsConst
            || field.IsImplicitlyDeclared
            || !EfQueryOperationAnalysis.IsOrDerivesFrom(field.Type, dbContext))
        {
            return;
        }

        var location = field.DeclaringSyntaxReferences.Length > 0
            ? field.DeclaringSyntaxReferences[0].GetSyntax().GetLocation()
            : field.Locations.FirstOrDefault() ?? Location.None;

        var evidence = $"Field '{field.Name}' is declared static and its type '{field.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}' resolves to EF Core DbContext.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, properties));
    }
}
