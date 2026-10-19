using EFDoctor.Analyzers;
using EFDoctor.CodeFixes;

namespace EFDoctor.Analyzers.Tests;

public sealed class AwaitAsyncCounterpartCodeFixTests
{
    [Fact]
    public void FixesTheAnalyzersDiagnosticId()
    {
        Assert.Equal(SyncDatabaseCallInAsyncAnalyzer.DiagnosticId, Assert.Single(new AwaitAsyncCounterpartCodeFixProvider().FixableDiagnosticIds));
    }

    [Theory]
    [Trait("Spec", "analyzer-code-fixes/Query terminal in an async method")]
    [Trait("Spec", "analyzer-code-fixes/SaveChanges, Find, and a token in scope")]
    [Trait("Spec", "analyzer-code-fixes/Member read from the result")]
    [InlineData("static async Task<object> Run(ShopContext db) { await Task.Yield(); return db.Orders.Where(o => o.Open).ToList(); }", "static async Task<object> Run(ShopContext db) { await Task.Yield(); return await db.Orders.Where(o => o.Open).ToListAsync(); }")]
    [InlineData("static async Task<object?> Run(ShopContext db, int id) { await Task.Yield(); return db.Orders.FirstOrDefault(o => o.Id == id); }", "static async Task<object?> Run(ShopContext db, int id) { await Task.Yield(); return await db.Orders.FirstOrDefaultAsync(o => o.Id == id); }")]
    [InlineData("static async Task Run(ShopContext db, CancellationToken ct) { await Task.Yield(); db.SaveChanges(); }", "static async Task Run(ShopContext db, CancellationToken ct) { await Task.Yield(); await db.SaveChangesAsync(ct); }")]
    [InlineData("static async Task<int> Run(ShopContext db, CancellationToken ct) { await Task.Yield(); return db.Orders.Count(o => o.Open); }", "static async Task<int> Run(ShopContext db, CancellationToken ct) { await Task.Yield(); return await db.Orders.CountAsync(o => o.Open, ct); }")]
    [InlineData("static async Task<object?> Run(ShopContext db, int id, CancellationToken ct) { await Task.Yield(); return db.Orders.Find(id); }", "static async Task<object?> Run(ShopContext db, int id, CancellationToken ct) { await Task.Yield(); return await db.Orders.FindAsync(id); }")]
    [InlineData("static async Task<int> Run(ShopContext db) { await Task.Yield(); return db.Orders.ToList().Count; }", "static async Task<int> Run(ShopContext db) { await Task.Yield(); return (await db.Orders.ToListAsync()).Count; }")]
    [InlineData("static Func<CancellationToken, Task<bool>> Run(ShopContext db) => async token => { await Task.Yield(); return db.Orders.Any(); };", "static Func<CancellationToken, Task<bool>> Run(ShopContext db) => async token => { await Task.Yield(); return await db.Orders.AnyAsync(token); };")]
    public async Task AwaitsTheAsyncCounterpart(string member, string expected)
    {
        var fixedSource = await CodeFixTestHarness.ApplyAsync(Source(member), new SyncDatabaseCallInAsyncAnalyzer(), new AwaitAsyncCounterpartCodeFixProvider());

        Assert.Equal(Source(expected), fixedSource);
        CodeFixTestHarness.AssertCompiles(fixedSource!);
    }

    [Fact]
    [Trait("Spec", "analyzer-code-fixes/Query terminal in an async method")]
    public async Task AddsTheEfCoreUsingDirectiveWhenMissing()
    {
        const string Before = """
            using System.Linq;
            using System.Threading.Tasks;

            static class Subject
            {
                static async Task<object> Run(Shop.ShopContext db) { await Task.Yield(); return db.Orders.ToList(); }
            }

            namespace Shop
            {
                sealed class Order { public int Id { get; set; } }

                sealed class ShopContext : Microsoft.EntityFrameworkCore.DbContext
                {
                    public Microsoft.EntityFrameworkCore.DbSet<Order> Orders => Set<Order>();
                }
            }
            """;

        var fixedSource = await CodeFixTestHarness.ApplyAsync(Before, new SyncDatabaseCallInAsyncAnalyzer(), new AwaitAsyncCounterpartCodeFixProvider());

        Assert.Contains("using System.Threading.Tasks;\nusing Microsoft.EntityFrameworkCore;\n", fixedSource, StringComparison.Ordinal);
        Assert.Contains("return await db.Orders.ToListAsync();", fixedSource, StringComparison.Ordinal);
        CodeFixTestHarness.AssertCompiles(fixedSource!);
    }

    [Theory]
    [Trait("Spec", "analyzer-code-fixes/Await not possible")]
    [InlineData("static readonly object Gate = new(); static async Task Run(ShopContext db) { await Task.Yield(); lock (Gate) { db.SaveChanges(); } }")]
    [InlineData("static async Task<int> Run(ShopContext db) { await Task.Yield(); return Queryable.Count(db.Orders); }")]
    public async Task WithholdsTheFixWhereAwaitIsNotPossible(string member)
    {
        Assert.Null(await CodeFixTestHarness.ApplyAsync(Source(member), new SyncDatabaseCallInAsyncAnalyzer(), new AwaitAsyncCounterpartCodeFixProvider()));
    }

    [Fact]
    public async Task FixAllRewritesEveryCall()
    {
        var source = Source("static async Task<int> Run(ShopContext db) { await Task.Yield(); var count = db.Orders.Count(); db.SaveChanges(); return count; }");

        var fixedSource = await CodeFixTestHarness.FixAllAsync(source, new SyncDatabaseCallInAsyncAnalyzer(), new AwaitAsyncCounterpartCodeFixProvider());

        Assert.Equal(Source("static async Task<int> Run(ShopContext db) { await Task.Yield(); var count = await db.Orders.CountAsync(); await db.SaveChangesAsync(); return count; }"), fixedSource);
    }

    private static string Source(string member) => $$"""
        using System;
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
