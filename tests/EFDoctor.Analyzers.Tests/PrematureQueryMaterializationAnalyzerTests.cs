using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class PrematureQueryMaterializationAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("return context.Entities.ToList().Where(entity => entity.Id > 10);");
        yield return Case("return context.Entities.AsTracking().ToList().Where(entity => entity.Id > 10);");
        yield return Case("return context.Entities.IgnoreQueryFilters().IgnoreAutoIncludes().ToList().Where(entity => entity.Id > 10);");
        yield return Case("return context.Entities.ToArray().Select(entity => entity.Name);");
        yield return Case("return context.Entities.ToList().OrderBy(entity => entity.Name);");
        yield return Case("return context.Entities.ToArray().OrderByDescending(entity => entity.Id);");
        yield return Case("return context.Entities.ToList().Skip(offset);");
        yield return Case("return context.Entities.ToArray().Take(5);");
        yield return Case("return context.Entities.Where(entity => entity.Active).ToList().Where(entity => entity.Name != null);");
        yield return Case("return Enumerable.Where(Enumerable.ToList(context.Entities), entity => entity.Id == 1);");
        yield return AsyncCase("return (await context.Entities.ToListAsync(token)).Where(entity => entity.Id >= minimum);");
        yield return AsyncCase("return (await context.Entities.ToArrayAsync()).Select(entity => new { entity.Id, entity.Name });");
        yield return AsyncCase("return Enumerable.Take(await EntityFrameworkQueryableExtensions.ToListAsync(context.Entities, token), 2);");
        yield return Case("return context.Set<Entity>().ToList().Where(entity => entity.Active && (entity.Id > 0 || entity.Name == null));");
        yield return Case("return context.Entities.ToList().Where(entity => entity.Id > 0).Select(entity => entity.Id);");
        yield return Case("var query = context.Entities.Where(entity => entity.Active); return query.ToList().Where(entity => entity.Id > 10);");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return SourceCase("static List<Entity> Run(TestContext context) => context.Entities.ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context) => context.Entities.Where(entity => entity.Id > 0).ToList();");
        yield return SourceCase("static IEnumerable<int> Run() => new[] { 1, 2 }.ToList().Where(value => value > 0);");
        yield return SourceCase("static IEnumerable<Entity> Run(IQueryable<Entity> query) => query.ToList().Where(entity => entity.Id > 0);");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context) => context.Entities.AsEnumerable().ToList().Where(entity => entity.Id > 0);");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context) { var rows = context.Entities.ToList(); return rows.Where(entity => entity.Id > 0); }");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context) => context.Entities.ToList().Distinct().Where(entity => entity.Id > 0);");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context) => context.Entities.ToList().Where((entity, index) => entity.Id > index);");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context) => context.Entities.ToList().Where(entity => IsGood(entity)); static bool IsGood(Entity entity) => true;");
        yield return SourceCase("static IEnumerable<string> Run(TestContext context) => context.Entities.ToList().Select(entity => entity.DisplayName);");
        yield return SourceCase("static IOrderedEnumerable<Entity> Run(TestContext context) => context.Entities.ToList().OrderBy(entity => entity.Name.Length);");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context) => context.Entities.ToList().Take(GetLimit()); static int GetLimit() => 5;");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context, Holder holder) => context.Entities.ToList().Where(entity => entity.Id > holder.Minimum);", "sealed class Holder { public int Minimum { get; set; } }");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context) => context.Entities.ToList().Where(entity => (entity.Id = 1) > 0);");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context) => context.Entities.ToList().Where(entity => ((dynamic)entity.Id) > 0);");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context, Metric threshold) => context.Entities.ToList().Where(entity => entity.Score > threshold);");
        yield return SourceCase("static IEnumerable<Entity> Run(Store store) => store.ToList().Where(entity => entity.Id > 0);", "sealed class Store { public List<Entity> ToList() => new(); }");
        yield return SourceCase("static object Run(TestContext context) => missing.ToList().Where(entity => entity.Id > 0);");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context) { /* ToList().Where(...) */ var text = \"ToArray().Select(...)\"; #if NEVER return context.Entities.ToList().Where(entity => true); #else return context.Entities; #endif }");
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsPositiveFixture(string source, int expectedCount)
    {
        Assert.Empty(AnalyzerTestHarness.GetCompilationDiagnostics(source).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var matches = (await AnalyzeAsync(source)).Where(static diagnostic => diagnostic.Id == PrematureQueryMaterializationAnalyzer.DiagnosticId);
        Assert.Equal(expectedCount, matches.Count());
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportNegativeFixture(string source)
    {
        Assert.DoesNotContain(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == PrematureQueryMaterializationAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task ReportsEveryMaterializerAtItsExactInvocationWithCompleteProperties()
    {
        var source = QuerySource("""
            static object Run(TestContext context)
            {
                var first = context.Entities.ToList().Where(entity => entity.Id > 0);
                var second = context.Entities.ToArray().Select(entity => entity.Name);
                return new[] { first, second };
            }
            """);

        var matches = (await AnalyzeAsync(source)).Where(static diagnostic => diagnostic.Id == PrematureQueryMaterializationAnalyzer.DiagnosticId).ToArray();

        Assert.Equal(2, matches.Length);
        Assert.Equal("context.Entities.ToList()", Text(matches[0]));
        Assert.Equal("context.Entities.ToArray()", Text(matches[1]));
        Assert.All(matches, static diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
            Assert.Contains("DbSet origin", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
            Assert.Contains("actual impact depends", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("small bounded results", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.OrdinalIgnoreCase);
            Assert.Equal("EFD004", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        });
    }

    [Fact]
    public async Task PragmaSuppressionPreservesUnsuppressedNeighbor()
    {
        var source = QuerySource("""
            static object Run(TestContext context)
            {
            #pragma warning disable EFD004 // Intentional client-side reuse of this bounded set.
                var suppressed = context.Entities.ToList().Where(entity => entity.Id > 0);
            #pragma warning restore EFD004
                var reported = context.Entities.ToArray().Take(2);
                return new[] { suppressed, reported };
            }
            """);

        Assert.Single(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == PrematureQueryMaterializationAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task EditorConfigurationSeverityNoneIsHonored()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Entities.ToList().Where(entity => entity.Id > 0);");

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new PrematureQueryMaterializationAnalyzer(),
            diagnosticId: PrematureQueryMaterializationAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SuppressMessageWithJustificationIsHonored()
    {
        var source = QuerySource("""
            [SuppressMessage("Performance", "EFD004", Justification = "The bounded results are reused by client-only logic.")]
            static object Run(TestContext context) => context.Entities.ToList().Where(entity => entity.Id > 0);
            """);

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public void DescriptorIsCompleteAndQualified()
    {
        Assert.Equal("EFD004", PrematureQueryMaterializationAnalyzer.Rule.Id);
        Assert.Equal("Query is materialized before SQL-capable composition", PrematureQueryMaterializationAnalyzer.Rule.Title.ToString());
        Assert.Equal(DiagnosticSeverity.Warning, PrematureQueryMaterializationAnalyzer.Rule.DefaultSeverity);
        Assert.Contains("may", PrematureQueryMaterializationAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("always", PrematureQueryMaterializationAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            analyzer: new PrematureQueryMaterializationAnalyzer(),
            diagnosticId: PrematureQueryMaterializationAnalyzer.DiagnosticId);

    private static object[] Case(string statement) => [QuerySource($"static object Run(TestContext context, int offset = 1) {{ {statement} }}"), 1];

    private static object[] AsyncCase(string statement) =>
        [QuerySource($"static async Task<object> Run(TestContext context, int minimum = 1, CancellationToken token = default) {{ {statement} }}"), 1];

    private static object[] SourceCase(string member, string additionalTypes = "") => [QuerySource(member, additionalTypes)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string QuerySource(string member, string additionalTypes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Entity
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public bool Active { get; set; }
            public Metric Score { get; set; }
            public string DisplayName => Name ?? string.Empty;
        }

        readonly struct Metric
        {
            public static bool operator >(Metric left, Metric right) => false;
            public static bool operator <(Metric left, Metric right) => false;
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Entity> Entities => Set<Entity>();
        }

        {{additionalTypes}}

        static class Subject
        {
            {{member}}
        }
        """;
}
