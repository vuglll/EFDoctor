using EFDoctor.Analyzers;
using EFDoctor.CodeFixes;

namespace EFDoctor.Analyzers.Tests;

public sealed class OrderByToThenByCodeFixTests
{
    [Fact]
    public void FixesTheAnalyzersDiagnosticId()
    {
        Assert.Equal(OrderByReplacesOrderingAnalyzer.DiagnosticId, Assert.Single(new OrderByToThenByCodeFixProvider().FixableDiagnosticIds));
    }

    [Theory]
    [Trait("Spec", "analyzer-code-fixes/OrderBy after OrderBy")]
    [Trait("Spec", "analyzer-code-fixes/Descending and static forms")]
    [Trait("Spec", "analyzer-code-fixes/Fix offered in the IDE")]
    [InlineData("context.Entities.OrderBy(e => e.Name).OrderBy(e => e.Id)", "context.Entities.OrderBy(e => e.Name).ThenBy(e => e.Id)")]
    [InlineData("context.Entities.OrderBy(e => e.Name).OrderByDescending(e => e.Id)", "context.Entities.OrderBy(e => e.Name).ThenByDescending(e => e.Id)")]
    [InlineData("context.Entities.OrderByDescending(e => e.Name).ThenBy(e => e.Id).OrderBy(e => e.CreatedUtc)", "context.Entities.OrderByDescending(e => e.Name).ThenBy(e => e.Id).ThenBy(e => e.CreatedUtc)")]
    [InlineData("Queryable.OrderBy(context.Entities.OrderBy(e => e.Name), e => e.Id)", "Queryable.ThenBy(context.Entities.OrderBy(e => e.Name), e => e.Id)")]
    [InlineData("context.Entities\n            .OrderBy(e => e.Name)\n            .OrderBy(e => e.Id)", "context.Entities\n            .OrderBy(e => e.Name)\n            .ThenBy(e => e.Id)")]
    public async Task ReplacesTheDiscardingOrderByWithThenBy(string query, string expected)
    {
        var fixedSource = await CodeFixTestHarness.ApplyAsync(Source(query), new OrderByReplacesOrderingAnalyzer(), new OrderByToThenByCodeFixProvider());

        Assert.Equal(Source(expected), fixedSource);
        CodeFixTestHarness.AssertCompiles(fixedSource!);
    }

    [Theory]
    [Trait("Spec", "analyzer-code-fixes/Unordered receiver")]
    [InlineData("context.Entities.OrderBy(e => e.Name).Where(e => e.Id > 0).OrderBy(e => e.Id)")]
    [InlineData("context.Entities.OrderBy(e => e.Name).AsNoTracking().OrderBy(e => e.Id)")]
    public async Task WithholdsTheFixWhenThenByWouldNotCompile(string query)
    {
        Assert.Null(await CodeFixTestHarness.ApplyAsync(Source(query), new OrderByReplacesOrderingAnalyzer(), new OrderByToThenByCodeFixProvider()));
    }

    [Fact]
    public async Task FixAllRewritesEveryOccurrence()
    {
        var source = Source("context.Entities.OrderBy(e => e.Name).OrderBy(e => e.Id)", second: "context.Entities.OrderBy(e => e.Id).OrderByDescending(e => e.CreatedUtc)");

        var fixedSource = await CodeFixTestHarness.FixAllAsync(source, new OrderByReplacesOrderingAnalyzer(), new OrderByToThenByCodeFixProvider());

        Assert.Equal(Source("context.Entities.OrderBy(e => e.Name).ThenBy(e => e.Id)", second: "context.Entities.OrderBy(e => e.Id).ThenByDescending(e => e.CreatedUtc)"), fixedSource);
    }

    private static string Source(string query, string second = "context.Entities") => $$"""
        using System;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        sealed class Entity
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public DateTime CreatedUtc { get; set; }
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Entity> Entities => Set<Entity>();
        }

        static class Subject
        {
            static object Run(TestContext context) => {{query}};

            static object Other(TestContext context) => {{second}};
        }
        """;
}
