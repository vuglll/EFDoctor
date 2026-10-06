using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers.Tests;

// Behavior the rules share when they run in a build (openspec/specs/analyzer-package).
public sealed class AnalyzerPackageTests
{
    private const string Source = """
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        sealed class Customer
        {
            public int Id { get; set; }
            public string Email { get; set; } = "";
        }

        sealed class ShopContext : DbContext
        {
            public DbSet<Customer> Customers => Set<Customer>();
        }

        static class Subject
        {
            static void Save(ShopContext context)
            {
                foreach (var i in new[] { 1 })
                {
                    context.SaveChanges();
                }
            }

            static object Find(ShopContext context, string email) =>
                context.Customers.Where(customer => customer.Email.ToLower() == email);
        }
        """;

    private static readonly Dictionary<string, string> MarkedTestProject = new()
    {
        [EfTestProjectDetection.IsTestProjectProperty] = "true",
    };

    [Fact]
    [Trait("Spec", "analyzer-package/High-confidence finding is a warning")]
    public async Task HighConfidenceFindingKeepsTheRuleSeverity()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(new SaveChangesInLoopAnalyzer()));

        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.DefaultSeverity);
    }

    [Fact]
    [Trait("Spec", "analyzer-package/Medium-confidence finding is a suggestion")]
    public async Task MediumConfidenceFindingIsASuggestion()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(new NonSargableCaseTransformAnalyzer()));

        Assert.Equal("medium", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "analyzer-package/Medium-confidence finding is a suggestion")]
    [InlineData(DiagnosticSeverity.Warning, "high", DiagnosticSeverity.Warning)]
    [InlineData(DiagnosticSeverity.Warning, "medium", DiagnosticSeverity.Info)]
    [InlineData(DiagnosticSeverity.Warning, "advisory", DiagnosticSeverity.Info)]
    [InlineData(DiagnosticSeverity.Info, "high", DiagnosticSeverity.Info)]
    [InlineData(DiagnosticSeverity.Info, "medium", DiagnosticSeverity.Info)]
    [InlineData(DiagnosticSeverity.Error, "medium", DiagnosticSeverity.Info)]
    [InlineData(DiagnosticSeverity.Hidden, "medium", DiagnosticSeverity.Hidden)]
    public void BuildSeverityNeverExceedsInfoBelowHighConfidence(DiagnosticSeverity ruleSeverity, string confidence, DiagnosticSeverity expected)
    {
        Assert.Equal(expected, EfDiagnostic.BuildSeverity(ruleSeverity, confidence));
    }

    [Fact]
    [Trait("Spec", "analyzer-package/Medium-only rules default to Info")]
    public void RulesThatNeverExceedMediumConfidenceDefaultToInfo()
    {
        Assert.All(
            new[]
            {
                MultipleCollectionIncludeAnalyzer.Rule,
                NonSargableCaseTransformAnalyzer.Rule,
                SyncDatabaseCallInAsyncAnalyzer.Rule,
                BulkUpdateDeleteAnalyzer.Rule,
                MultipleEnumerationAnalyzer.Rule,
            },
            static rule => Assert.Equal(DiagnosticSeverity.Info, rule.DefaultSeverity));
    }

    [Theory]
    [Trait("Spec", "analyzer-package/Configured severity replaces the default")]
    [InlineData(ReportDiagnostic.Warn, DiagnosticSeverity.Warning)]
    [InlineData(ReportDiagnostic.Error, DiagnosticSeverity.Error)]
    public async Task ConfiguredSeverityReplacesTheSuggestion(ReportDiagnostic configured, DiagnosticSeverity expected)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(new NonSargableCaseTransformAnalyzer(), configuredSeverity: configured));

        Assert.Equal(expected, diagnostic.Severity);
    }

    [Fact]
    [Trait("Spec", "analyzer-package/Configured severity replaces the default")]
    public async Task SeverityNoneSilencesASuggestion()
    {
        Assert.Empty(await AnalyzeAsync(new NonSargableCaseTransformAnalyzer(), configuredSeverity: ReportDiagnostic.Suppress));
    }

    [Fact]
    [Trait("Spec", "analyzer-package/Project marked as a test project")]
    public async Task ProjectMarkedAsTestProjectIsSkipped()
    {
        Assert.Empty(await AnalyzeAsync(new SaveChangesInLoopAnalyzer(), globalOptions: MarkedTestProject));
    }

    [Fact]
    [Trait("Spec", "analyzer-package/Test-framework reference")]
    public async Task ProjectReferencingATestFrameworkIsSkipped()
    {
        Assert.Empty(await AnalyzeAsync(new SaveChangesInLoopAnalyzer(), includeTestFramework: true));
    }

    [Fact]
    [Trait("Spec", "analyzer-package/Opt back in")]
    public async Task OptInAnalyzesATestProject()
    {
        var optedIn = new Dictionary<string, string>(MarkedTestProject)
        {
            [EfTestProjectDetection.AnalyzeTestProjectsProperty] = "true",
        };

        var diagnostic = Assert.Single(await AnalyzeAsync(new SaveChangesInLoopAnalyzer(), globalOptions: optedIn, includeTestFramework: true));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "analyzer-package/Production project")]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("")]
    public async Task ProductionProjectIsAnalyzed(string? isTestProject)
    {
        var options = isTestProject is null
            ? null
            : new Dictionary<string, string> { [EfTestProjectDetection.IsTestProjectProperty] = isTestProject };

        Assert.Single(await AnalyzeAsync(new SaveChangesInLoopAnalyzer(), globalOptions: options));
    }

    // The analyzers compile against a Roslyn that predates this operation kind, so EFD027 matches
    // it by value.
    [Fact]
    public void CollectionExpressionOperationKindMatchesRoslyn()
    {
        Assert.Equal((int)OperationKind.CollectionExpression, EfQueryOperationAnalysis.CollectionExpressionOperationKind);
    }

    private static async Task<Diagnostic[]> AnalyzeAsync(
        DiagnosticAnalyzer analyzer,
        ReportDiagnostic? configuredSeverity = null,
        IReadOnlyDictionary<string, string>? globalOptions = null,
        bool includeTestFramework = false)
    {
        var id = analyzer.SupportedDiagnostics.Single().Id;
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            Source,
            configuredSeverity: configuredSeverity,
            analyzer: analyzer,
            diagnosticId: id,
            globalOptions: globalOptions,
            includeTestFramework: includeTestFramework);
        return diagnostics.Where(diagnostic => diagnostic.Id == id).ToArray();
    }
}
