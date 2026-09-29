using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using EFDoctor.Analyzers;

namespace EFDoctor.Analyzers.Tests;

public sealed class OrderByReplacesOrderingAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc).ToList();", 1);
        yield return Case("static object Run(TestContext context) => context.Entities.OrderByDescending(e => e.Name).OrderBy(e => e.CreatedUtc).ToList();", 1);
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderByDescending(e => e.CreatedUtc).ToList();", 1);
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).ThenBy(e => e.Id).OrderBy(e => e.CreatedUtc).ToList();", 1);
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).ThenByDescending(e => e.Id).OrderByDescending(e => e.CreatedUtc).ToList();", 1);
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).Where(e => e.Id > 0).OrderBy(e => e.CreatedUtc).ToList();", 1);
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).Include(e => e.Children).AsNoTracking().OrderBy(e => e.CreatedUtc).ToList();", 1);
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).AsSplitQuery().TagWith(\"list\").IgnoreQueryFilters().OrderBy(e => e.CreatedUtc).ToList();", 1);
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc).OrderBy(e => e.Id).ToList();", 2);
        yield return Case("static object Run(TestContext context) => Queryable.OrderBy(context.Entities.OrderBy(e => e.Name), e => e.CreatedUtc).ToList();", 1);
        yield return Case("static object Run(TestContext context) => context.Set<Entity>().Where(e => e.Id > 0).OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc).ToList();", 1);
        yield return Case("static object Run(TestContext context) { var query = context.Entities.Where(e => e.Id > 0); return query.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc).ToList(); }", 1);
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc).Skip(10).Take(5).ToList();", 1);
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).ThenBy(e => e.CreatedUtc).ToList();");
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).ToList();");
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).Take(10).OrderBy(e => e.CreatedUtc).ToList();");
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).Skip(10).OrderBy(e => e.CreatedUtc).ToList();");
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).Distinct().OrderBy(e => e.CreatedUtc).ToList();");
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).Select(e => e.CreatedUtc).OrderBy(d => d).ToList();");
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).GroupBy(e => e.Name).OrderBy(g => g.Key).ToList();");
        yield return Case("static object Run(TestContext context) { var query = context.Entities.OrderBy(e => e.Name); return query.OrderBy(e => e.CreatedUtc).ToList(); }");
        yield return Case("static object Run(TestContext context, bool byDate) { IQueryable<Entity> query = context.Entities.OrderBy(e => e.Name); if (byDate) { query = query.OrderBy(e => e.CreatedUtc); } return query.ToList(); }");
        yield return Case("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).AsEnumerable().OrderBy(e => e.CreatedUtc).ToList();");
        yield return Case("static object Run(TestContext context) => context.Entities.ToList().OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc).ToList();");
        yield return Case("static object Run(IQueryable<Entity> query) => query.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc).ToList();");
        yield return Case("static object Run(TestContext context) => Sorting.OrderBy(context.Entities.OrderBy(e => e.Name), 1);");
        yield return Case("static object Run(TestContext context) => context.Entities.Where(e => e.Id > 0).OrderBy(e => e.CreatedUtc).ToList();");
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    [Trait("Spec", "efd029-orderby-replaces-ordering/OrderBy followed by OrderBy")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Descending variants")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Existing ThenBy is also discarded")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Pass-through operators between the orderings")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Three consecutive OrderBy calls")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Static invocation syntax")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Analyzer fixture suite")]
    public async Task ReportsPositiveFixture(string source, int expectedCount)
    {
        var matches = await AnalyzeAsync(source);
        Assert.Equal(expectedCount, matches.Count(static diagnostic => diagnostic.Id == OrderByReplacesOrderingAnalyzer.DiagnosticId));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    [Trait("Spec", "efd029-orderby-replaces-ordering/ThenBy after OrderBy")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Paging between the orderings")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Shape-changing operator between the orderings")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Earlier ordering stored in a local")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/In-memory ordering")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Unproven or unrelated source")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Single ordering")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Analyzer fixture suite")]
    public async Task DoesNotReportNegativeFixture(string source)
    {
        var matches = await AnalyzeAsync(source);
        Assert.DoesNotContain(matches, static diagnostic => diagnostic.Id == OrderByReplacesOrderingAnalyzer.DiagnosticId);
    }

    [Fact]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Exact diagnostic location")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Qualified remediation")]
    public async Task ReportsReplacingCallWithTightSpanAndCompleteProperties()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).ThenBy(e => e.Id).OrderByDescending(e => e.CreatedUtc).ToList();");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("OrderByDescending(e => e.CreatedUtc)", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("This OrderByDescending discards the earlier ordering of the same EF Core query; use ThenBy to add a secondary sort", diagnostic.GetMessage());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD029", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal(OrderByReplacesOrderingAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Equal(OrderByReplacesOrderingAnalyzer.Remediation, diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]);

        var evidence = diagnostic.Properties[DiagnosticPropertyNames.Evidence]!;
        Assert.Contains("already ordered by 'OrderBy -> ThenBy'", evidence, StringComparison.Ordinal);
        Assert.Contains("which this OrderByDescending replaces", evidence, StringComparison.Ordinal);
        Assert.Contains("DbSet origin 'context.Entities'", evidence, StringComparison.Ordinal);

        Assert.Contains("ThenBy", OrderByReplacesOrderingAnalyzer.Remediation, StringComparison.Ordinal);
        Assert.Contains("ThenByDescending", OrderByReplacesOrderingAnalyzer.Remediation, StringComparison.Ordinal);
        Assert.Contains("delete the earlier ordering", OrderByReplacesOrderingAnalyzer.Remediation, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Static invocation syntax")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Exact diagnostic location")]
    public async Task StaticSyntaxSpansWholeInvocation()
    {
        var source = QuerySource("static object Run(TestContext context) => Queryable.OrderBy(context.Entities.OrderBy(e => e.Name), e => e.CreatedUtc);");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("Queryable.OrderBy(context.Entities.OrderBy(e => e.Name), e => e.CreatedUtc)", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Three consecutive OrderBy calls")]
    public async Task EachReplacingCallIsReportedOnce()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc).OrderBy(e => e.Id);");

        var spans = (await AnalyzeAsync(source))
            .Select(static diagnostic => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan))
            .OrderBy(static text => text, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["OrderBy(e => e.CreatedUtc)", "OrderBy(e => e.Id)"], spans);
    }

    [Fact]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Pragma suppression")]
    public async Task HonorsPragmaSuppressionWhileSiblingStillReports()
    {
        var source = QuerySource("""
            #pragma warning disable EFD029 // Reviewed: the override is intended.
            static object Suppressed(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc);
            #pragma warning restore EFD029
            static object Reported(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc);
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", LineOf(diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Editor configuration suppression")]
    public async Task HonorsEditorConfigSuppression()
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            QuerySource("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc);"),
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new OrderByReplacesOrderingAnalyzer(),
            diagnosticId: OrderByReplacesOrderingAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd029-orderby-replaces-ordering/SuppressMessage attribute")]
    public async Task HonorsSuppressMessageAttributeWhileSiblingStillReports()
    {
        var source = QuerySource("""
            [SuppressMessage("Correctness", "EFD029", Justification = "The override is intended.")]
            static object Suppressed(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc);
            static object Reported(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc);
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", LineOf(diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Generated or malformed code")]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + QuerySource("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderBy(e => e.CreatedUtc);")));
    }

    [Fact]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Generated or malformed code")]
    public async Task DoesNotReportOrThrowOnUnresolvedCode()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Entities.OrderBy(e => e.Name).OrderBy(e => e.Missing);");

        Assert.Contains(AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Unproven or unrelated source")]
    public async Task DoesNotReportWithoutEntityFrameworkReference()
    {
        var source = """
            using System.Linq;
            static class Subject
            {
                static object Run(IQueryable<string> query) => query.OrderBy(value => value.Length).OrderBy(value => value);
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeEntityFramework: false,
            analyzer: new OrderByReplacesOrderingAnalyzer(),
            diagnosticId: OrderByReplacesOrderingAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Analyzer fixture suite")]
    public void FixturesCompileExceptDeliberatelyMalformedCase()
    {
        var failures = PositiveCases().Concat(NegativeCases())
            .Select(static fixture => (string)fixture[0])
            .SelectMany(static source => AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => $"{diagnostic.Id} {diagnostic.GetMessage()}"))
            .ToArray();

        Assert.Empty(failures);
        Assert.True(PositiveCases().Count() >= 10);
        Assert.True(NegativeCases().Count() >= 10);
    }

    [Fact]
    public void DescriptorUsesCorrectnessWarningMetadata()
    {
        Assert.Equal("EFD029", OrderByReplacesOrderingAnalyzer.Rule.Id);
        Assert.Equal("OrderBy discards an earlier ordering", OrderByReplacesOrderingAnalyzer.Rule.Title.ToString());
        Assert.Equal("Correctness", OrderByReplacesOrderingAnalyzer.Rule.Category);
        Assert.Equal(DiagnosticSeverity.Warning, OrderByReplacesOrderingAnalyzer.Rule.DefaultSeverity);
        Assert.True(OrderByReplacesOrderingAnalyzer.Rule.IsEnabledByDefault);
    }

    private static string LineOf(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString();

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new OrderByReplacesOrderingAnalyzer(),
            diagnosticId: OrderByReplacesOrderingAnalyzer.DiagnosticId);

    private static object[] Case(string member, int expectedCount) => [QuerySource(member), expectedCount];

    private static object[] Case(string member) => [QuerySource(member)];

    private static string QuerySource(string member) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        sealed class Entity
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public DateTime CreatedUtc { get; set; }
            public List<Child> Children { get; set; } = new();
        }

        sealed class Child
        {
            public int Id { get; set; }
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Entity> Entities => Set<Entity>();
        }

        static class Sorting
        {
            public static IQueryable<Entity> OrderBy(IQueryable<Entity> source, int key) => source;
        }

        static class Subject
        {
            {{member}}
        }
        """;
}
