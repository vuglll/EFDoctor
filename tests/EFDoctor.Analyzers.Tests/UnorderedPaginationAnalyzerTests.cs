using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using EFDoctor.Analyzers;

namespace EFDoctor.Analyzers.Tests;

public sealed class UnorderedPaginationAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.Skip(10).ToList();", 1);
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.Skip(10).Take(5).ToList();", 1);
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.Where(entity => entity.Id > 0).Skip(10).Take(5).ToList();", 1);
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.Skip(5).OrderBy(entity => entity.Id).ToList();", 1);
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) { var query = context.Entities.Where(entity => entity.Id > 0); return query.Skip(5).ToList(); }", 1);
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) { var query = context.Entities.Where(entity => entity.Id > 0); query = query.Where(entity => entity.CreatedUtc > System.DateTime.MinValue); return query.Skip(5).ToList(); }", 1);
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.OrderBy(entity => entity.Id).Skip(10).Take(5).ToList();");
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.OrderByDescending(entity => entity.CreatedUtc).Skip(10).ToList();");
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.OrderBy(entity => entity.Id).ThenBy(entity => entity.CreatedUtc).Skip(10).Take(5).ToList();");
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.Take(5).ToList();");
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.Where(entity => entity.Id > 0).Take(100).ToList();");
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.ToList().Skip(10).Take(5).ToList();");
        yield return Case("static System.Collections.Generic.List<Entity> Run(System.Linq.IQueryable<Entity> query) => query.Skip(10).Take(5).ToList();");
        yield return Case("static System.Collections.Generic.List<int> Run() => new System.Collections.Generic.List<int> { 1, 2, 3 }.Skip(1).Take(1).ToList();");
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) { var query = context.Entities.OrderBy(entity => entity.Id); return query.Skip(5).ToList(); }");
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context) { var query = context.Entities.Where(entity => entity.Id > 0); query = query.OrderBy(entity => entity.CreatedUtc); return query.Skip(5).ToList(); }");
        yield return Case("static System.Collections.Generic.List<Entity> Run(TestContext context, bool sort) { var query = context.Entities.Where(entity => entity.Id > 0); if (sort) { query = query.OrderBy(entity => entity.Id); } return query.Skip(5).ToList(); }");
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsPositiveFixture(string source, int expectedCount)
    {
        var matches = await AnalyzeAsync(source);
        Assert.Equal(expectedCount, matches.Count(static diagnostic => diagnostic.Id == UnorderedPaginationAnalyzer.DiagnosticId));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportNegativeFixture(string source)
    {
        var matches = await AnalyzeAsync(source);
        Assert.DoesNotContain(matches, static diagnostic => diagnostic.Id == UnorderedPaginationAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task SkipThenTakeYieldsSingleFindingOnTheTake()
    {
        var source = QuerySource("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.Skip(10).Take(5).ToList();");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        var text = diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
        Assert.Contains("Take(5)", text, StringComparison.Ordinal);
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
    }

    [Fact]
    public async Task SkipFindingCarriesEvidenceAndDocumentationKey()
    {
        var source = QuerySource("static System.Collections.Generic.List<Entity> Run(TestContext context) => context.Entities.Skip(10).ToList();");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains("no ordering operator precedes", diagnostic.Properties[DiagnosticPropertyNames.Evidence]!, StringComparison.Ordinal);
        Assert.Equal("EFD014", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
    }

    [Fact]
    public async Task PragmaSuppressionIsHonored()
    {
        var source = QuerySource("""
            static System.Collections.Generic.List<Entity> Run(TestContext context)
            {
            #pragma warning disable EFD014
                var page = context.Entities.Skip(10).Take(5).ToList();
            #pragma warning restore EFD014
                return page;
            }
            """);

        Assert.DoesNotContain(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == UnorderedPaginationAnalyzer.DiagnosticId);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            QuerySourceIfNeeded(source),
            analyzer: new UnorderedPaginationAnalyzer(),
            diagnosticId: UnorderedPaginationAnalyzer.DiagnosticId);

    private static object[] Case(string member, int expectedCount) => [QuerySource(member), expectedCount];

    private static object[] Case(string member) => [QuerySource(member)];

    private static string QuerySourceIfNeeded(string source) =>
        source.Contains("class TestContext", StringComparison.Ordinal) ? source : QuerySource(source);

    private static string QuerySource(string member) => $$"""
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        sealed class Entity
        {
            public int Id { get; set; }
            public DateTime CreatedUtc { get; set; }
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
