using EFDoctor.Analyzers;
using EFDoctor.CodeFixes;

namespace EFDoctor.Analyzers.Tests;

public sealed class ReduceInQueryCodeFixTests
{
    [Fact]
    public void FixesTheAnalyzersDiagnosticId()
    {
        Assert.Equal(MaterializeThenReduceAnalyzer.DiagnosticId, Assert.Single(new ReduceInQueryCodeFixProvider().FixableDiagnosticIds));
    }

    [Theory]
    [Trait("Spec", "analyzer-code-fixes/Synchronous reduction")]
    [InlineData("static object Run(ShopContext db) => db.Orders.ToList().First(o => o.Open);", "static object Run(ShopContext db) => db.Orders.First(o => o.Open);")]
    [InlineData("static object? Run(ShopContext db) => db.Orders.Where(o => o.Open).ToList().FirstOrDefault();", "static object? Run(ShopContext db) => db.Orders.Where(o => o.Open).FirstOrDefault();")]
    [InlineData("static int Run(ShopContext db) => db.Orders.ToList().Count();", "static int Run(ShopContext db) => db.Orders.Count();")]
    [InlineData("static int Run(ShopContext db) => db.Orders.ToList().Count;", "static int Run(ShopContext db) => db.Orders.Count();")]
    [InlineData("static int Run(ShopContext db) => db.Orders.ToArray().Length;", "static int Run(ShopContext db) => db.Orders.Count();")]
    [InlineData("static bool Run(ShopContext db) => db.Orders.ToList().Any(o => o.Open);", "static bool Run(ShopContext db) => db.Orders.Any(o => o.Open);")]
    [InlineData("static decimal Run(ShopContext db) => db.Orders.ToList().Sum(o => o.Total);", "static decimal Run(ShopContext db) => db.Orders.Sum(o => o.Total);")]
    public async Task MovesASynchronousReductionIntoTheQuery(string member, string expected)
    {
        var fixedSource = await CodeFixTestHarness.ApplyAsync(Source(member), new MaterializeThenReduceAnalyzer(), new ReduceInQueryCodeFixProvider());

        Assert.Equal(Source(expected), fixedSource);
        CodeFixTestHarness.AssertCompiles(fixedSource!);
    }

    [Theory]
    [Trait("Spec", "analyzer-code-fixes/Awaited reduction with a token")]
    [InlineData("static async Task<bool> Run(ShopContext db, CancellationToken token) => (await db.Orders.ToListAsync(token)).Any(o => o.Open);", "static async Task<bool> Run(ShopContext db, CancellationToken token) => await db.Orders.AnyAsync(o => o.Open, token);")]
    [InlineData("static async Task<int> Run(ShopContext db) => (await db.Orders.ToListAsync()).Count;", "static async Task<int> Run(ShopContext db) => await db.Orders.CountAsync();")]
    [InlineData("static async Task<Order> Run(ShopContext db) => (await db.Orders.ToArrayAsync()).First();", "static async Task<Order> Run(ShopContext db) => await db.Orders.FirstAsync();")]
    [InlineData("static async Task<int> Run(ShopContext db, CancellationToken token) => (await db.Orders.Where(o => o.Open).ToArrayAsync(token)).Length;", "static async Task<int> Run(ShopContext db, CancellationToken token) => await db.Orders.Where(o => o.Open).CountAsync(token);")]
    public async Task MovesAnAwaitedReductionIntoTheQuery(string member, string expected)
    {
        var fixedSource = await CodeFixTestHarness.ApplyAsync(Source(member), new MaterializeThenReduceAnalyzer(), new ReduceInQueryCodeFixProvider());

        Assert.Equal(Source(expected), fixedSource);
        CodeFixTestHarness.AssertCompiles(fixedSource!);
    }

    [Theory]
    [Trait("Spec", "analyzer-code-fixes/Stored or statically reduced result")]
    [InlineData("static bool Run(ShopContext db) { var orders = db.Orders.ToList(); return orders.Count > 0; }")]
    [InlineData("static int Run(ShopContext db) => Enumerable.Count(db.Orders.ToList());")]
    public async Task WithholdsTheFixForStoredOrStaticForms(string member)
    {
        Assert.Null(await CodeFixTestHarness.ApplyAsync(Source(member), new MaterializeThenReduceAnalyzer(), new ReduceInQueryCodeFixProvider()));
    }

    [Fact]
    public async Task FixAllRewritesEveryReduction()
    {
        var source = Source("static int Run(ShopContext db) => db.Orders.ToList().Count() + db.Orders.ToArray().Length;");

        var fixedSource = await CodeFixTestHarness.FixAllAsync(source, new MaterializeThenReduceAnalyzer(), new ReduceInQueryCodeFixProvider());

        Assert.Equal(Source("static int Run(ShopContext db) => db.Orders.Count() + db.Orders.Count();"), fixedSource);
    }

    private static string Source(string member) => $$"""
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Order
        {
            public int Id { get; set; }
            public bool Open { get; set; }
            public decimal Total { get; set; }
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
