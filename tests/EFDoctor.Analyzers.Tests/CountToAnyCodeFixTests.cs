using EFDoctor.Analyzers;
using EFDoctor.CodeFixes;

namespace EFDoctor.Analyzers.Tests;

public sealed class CountToAnyCodeFixTests
{
    [Fact]
    public void FixesTheAnalyzersDiagnosticId()
    {
        Assert.Equal(CountUsedForExistenceAnalyzer.DiagnosticId, Assert.Single(new CountToAnyCodeFixProvider().FixableDiagnosticIds));
    }

    [Theory]
    [Trait("Spec", "analyzer-code-fixes/Count compared for existence")]
    [Trait("Spec", "analyzer-code-fixes/Emptiness test and reversed operands")]
    [InlineData("db.Orders.Count(o => o.Open) > 0", "db.Orders.Any(o => o.Open)")]
    [InlineData("db.Orders.Count() != 0", "db.Orders.Any()")]
    [InlineData("db.Orders.Where(o => o.Open).Count() >= 1", "db.Orders.Where(o => o.Open).Any()")]
    [InlineData("db.Orders.Count() == 0", "!db.Orders.Any()")]
    [InlineData("db.Orders.Count() <= 0", "!db.Orders.Any()")]
    [InlineData("db.Orders.Count() < 1", "!db.Orders.Any()")]
    [InlineData("0 < db.Orders.Count()", "db.Orders.Any()")]
    [InlineData("0 == db.Orders.Count(o => o.Open)", "!db.Orders.Any(o => o.Open)")]
    [InlineData("(db.Orders.Count()) > 0", "(db.Orders.Any())")]
    [InlineData("Queryable.Count(db.Orders) > 0", "Queryable.Any(db.Orders)")]
    public async Task ReplacesSynchronousCountComparisons(string comparison, string expected)
    {
        var fixedSource = await CodeFixTestHarness.ApplyAsync(Sync(comparison), new CountUsedForExistenceAnalyzer(), new CountToAnyCodeFixProvider());

        Assert.Equal(Sync(expected), fixedSource);
        CodeFixTestHarness.AssertCompiles(fixedSource!);
    }

    [Theory]
    [Trait("Spec", "analyzer-code-fixes/Awaited CountAsync")]
    [InlineData("await db.Orders.CountAsync(o => o.Open, token) >= 1", "await db.Orders.AnyAsync(o => o.Open, token)")]
    [InlineData("await db.Orders.CountAsync() < 1", "!await db.Orders.AnyAsync()")]
    [InlineData("(await db.Orders.CountAsync()) > 0", "(await db.Orders.AnyAsync())")]
    public async Task ReplacesAwaitedCountAsyncComparisons(string comparison, string expected)
    {
        var fixedSource = await CodeFixTestHarness.ApplyAsync(Async(comparison), new CountUsedForExistenceAnalyzer(), new CountToAnyCodeFixProvider());

        Assert.Equal(Async(expected), fixedSource);
        CodeFixTestHarness.AssertCompiles(fixedSource!);
    }

    [Theory]
    [Trait("Spec", "analyzer-code-fixes/Stored count or null-checked FirstOrDefault")]
    [InlineData("static bool Run(ShopContext db) { var count = db.Orders.Count(); return count > 0; }")]
    [InlineData("static bool Run(ShopContext db, int id) { var order = db.Orders.FirstOrDefault(o => o.Id == id); return order != null; }")]
    public async Task WithholdsTheFixForMultiStatementShapes(string member)
    {
        Assert.Null(await CodeFixTestHarness.ApplyAsync(Source(member), new CountUsedForExistenceAnalyzer(), new CountToAnyCodeFixProvider()));
    }

    [Fact]
    public async Task FixAllRewritesEveryComparison()
    {
        var source = Source("static bool Run(ShopContext db) => db.Orders.Count() > 0 && db.Orders.Count(o => o.Open) == 0;");

        var fixedSource = await CodeFixTestHarness.FixAllAsync(source, new CountUsedForExistenceAnalyzer(), new CountToAnyCodeFixProvider());

        Assert.Equal(Source("static bool Run(ShopContext db) => db.Orders.Any() && !db.Orders.Any(o => o.Open);"), fixedSource);
    }

    private static string Sync(string comparison) => Source($"static bool Run(ShopContext db) => {comparison};");

    private static string Async(string comparison) => Source($"static async Task<bool> Run(ShopContext db, CancellationToken token) => {comparison};");

    private static string Source(string member) => $$"""
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Order
        {
            public int Id { get; set; }
            public bool Open { get; set; }
        }

        sealed class ShopContext : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();
        }

        static class Subject
        {
            {{member}}
        }
        """;
}
