using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using EFDoctor.Analyzers;

namespace EFDoctor.Analyzers.Tests;

public sealed class StringComparisonInPredicateAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context, string value) => context.Entities.Where(entity => entity.Name.Equals(value, System.StringComparison.OrdinalIgnoreCase)).ToList();", 1);
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context, string value) => context.Entities.Where(entity => entity.Name.StartsWith(value, System.StringComparison.OrdinalIgnoreCase)).ToList();", 1);
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context, string value) => context.Entities.Where(entity => entity.Name.EndsWith(value, System.StringComparison.Ordinal)).ToList();", 1);
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context, string value) => context.Entities.Where(entity => entity.Name.Contains(value, System.StringComparison.OrdinalIgnoreCase)).ToList();", 1);
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context, string value) => context.Entities.Where(entity => string.Equals(entity.Name, value, System.StringComparison.Ordinal)).ToList();", 1);
        yield return Case("static Entity Run(TestContext context, string value) => context.Entities.First(entity => entity.Name.Equals(value, System.StringComparison.Ordinal));", 1);
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context, string value) { var query = context.Entities.AsNoTracking(); return query.Where(entity => entity.Name.Equals(value, System.StringComparison.OrdinalIgnoreCase)).ToList(); }", 1);
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context, string value) => context.Entities.Where(entity => entity.Name == value).ToList();");
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context, string value) => context.Entities.Where(entity => entity.Name.StartsWith(value)).ToList();");
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context, string value) => context.Entities.ToList().Where(entity => entity.Name.Equals(value, System.StringComparison.Ordinal)).ToList();");
        yield return Case("static System.Collections.Generic.List<Entity> Run(System.Linq.IQueryable<Entity> query, string value) => query.Where(entity => entity.Name.Equals(value, System.StringComparison.Ordinal)).ToList();");
        yield return Case("static bool Run(string left, string right) => left.Equals(right, System.StringComparison.Ordinal);");
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsPositiveFixture(string source, int expectedCount)
    {
        var matches = await AnalyzeAsync(source);
        Assert.Equal(expectedCount, matches.Count(static diagnostic => diagnostic.Id == StringComparisonInPredicateAnalyzer.DiagnosticId));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportNegativeFixture(string source)
    {
        var matches = await AnalyzeAsync(source);
        Assert.DoesNotContain(matches, static diagnostic => diagnostic.Id == StringComparisonInPredicateAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task FindingCarriesEvidenceConfidenceAndDocumentationKey()
    {
        var source = QuerySource("static System.Collections.Generic.List<Entity> Run(TestContext context, string value) => context.Entities.Where(entity => entity.Name.Equals(value, System.StringComparison.Ordinal)).ToList();");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains("StringComparison", diagnostic.Properties[DiagnosticPropertyNames.Evidence]!, StringComparison.Ordinal);
        Assert.Equal("EFD022", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        var text = diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
        Assert.Contains("Equals", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PragmaSuppressionIsHonored()
    {
        var source = QuerySource("""
            static System.Collections.Generic.List<Entity> Run(TestContext context, string value)
            {
            #pragma warning disable EFD022
                var rows = context.Entities.Where(entity => entity.Name.Equals(value, System.StringComparison.Ordinal)).ToList();
            #pragma warning restore EFD022
                return rows;
            }
            """);

        Assert.DoesNotContain(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == StringComparisonInPredicateAnalyzer.DiagnosticId);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source.Contains("class TestContext", StringComparison.Ordinal) ? source : QuerySource(source),
            analyzer: new StringComparisonInPredicateAnalyzer(),
            diagnosticId: StringComparisonInPredicateAnalyzer.DiagnosticId);

    private static object[] Case(string member, int expectedCount) => [QuerySource(member), expectedCount];

    private static object[] Case(string member) => [QuerySource(member)];

    private static string QuerySource(string member) => $$"""
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        sealed class Entity
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Entity> Entities => Set<Entity>();
        }

        static class Subject
        {
            {{member}}
        }
        """;
}
