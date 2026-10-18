using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class QueryOutlivesContextAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static IQueryable<Order> Run() { using var db = new ShopContext(); return db.Orders.Where(o => o.Open); }", "db.Orders.Where(o => o.Open)");
        yield return Case("static IQueryable<Order> Run(IDbContextFactory<ShopContext> factory) { using var db = factory.CreateDbContext(); return db.Orders; }", "db.Orders");
        yield return Case("static IQueryable<Order> Run() { using (var db = new ShopContext()) { return db.Set<Order>().OrderBy(o => o.Id); } }", "db.Set<Order>().OrderBy(o => o.Id)");
        yield return Case("static IEnumerable<Order> Run() { using var db = new ShopContext(); return db.Orders.Where(o => o.Open); }", "db.Orders.Where(o => o.Open)");
        yield return Case("static IEnumerable<Order> Run() { using var db = new ShopContext(); return db.Orders.Where(o => o.Open).AsEnumerable(); }", "db.Orders.Where(o => o.Open).AsEnumerable()");
        yield return Case("static IAsyncEnumerable<Order> Run() { using var db = new ShopContext(); return db.Orders.AsAsyncEnumerable(); }", "db.Orders.AsAsyncEnumerable()");
        yield return Case("static async Task<IQueryable<Order>> Run(IDbContextFactory<ShopContext> factory) { await using var db = await factory.CreateDbContextAsync(); return db.Orders; }", "db.Orders");
        yield return Case("static IQueryable<Order> Run(bool open) { using var db = new ShopContext(); var query = db.Orders.AsNoTracking(); if (open) { query = query.Where(o => o.Open); } return query; }", "query");
        yield return Case("static IQueryable<Order> Run(IServiceProvider provider) { using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ShopContext>(); return db.Orders; }", "db.Orders");
        yield return Case("static IQueryable<Order> Run(IServiceProvider provider) { using (var scope = provider.CreateScope()) { var db = (ShopContext)scope.ServiceProvider.GetService(typeof(ShopContext))!; return db.Orders; } }", "db.Orders");
        yield return Case("static IQueryable<Order> Run(bool open) { using var db = new ShopContext(); if (open) { return db.Orders.Where(o => o.Open); } return Enumerable.Empty<Order>().AsQueryable(); }", "db.Orders.Where(o => o.Open)");
        yield return Case("static IQueryable<Order> Run(IServiceProvider provider) { using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetService<ShopContext>()!; return db.Orders.Where(o => o.Open); }", "db.Orders.Where(o => o.Open)");
        yield return Case("static Func<IQueryable<Order>> Run() => () => { using var db = new ShopContext(); return db.Orders; };", "db.Orders");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static List<Order> Run() { using var db = new ShopContext(); return db.Orders.Where(o => o.Open).ToList(); }");
        yield return Negative("static async Task<List<Order>> Run() { await using var db = new ShopContext(); return await db.Orders.ToListAsync(); }");
        yield return Negative("static Order[] Run() { using var db = new ShopContext(); return db.Orders.ToArray(); }");
        yield return Negative("static int Run() { using var db = new ShopContext(); return db.Orders.Count(); }");
        yield return Negative("static IQueryable<Order> Run(ShopContext db) => db.Orders.Where(o => o.Open);");
        yield return Negative("static IQueryable<Order> Run() { var db = new ShopContext(); return db.Orders; }");
        yield return Negative("static IQueryable<Order> Run() { IQueryable<Order> result; using (var db = new ShopContext()) { result = db.Orders; } return result; }");
        yield return Negative("static IEnumerable<Order> Run() { using var db = new ShopContext(); foreach (var order in db.Orders) { yield return order; } }");
        yield return Negative("static List<Order> Run() { using var db = new ShopContext(); Func<IQueryable<Order>> later = () => db.Orders; return later().ToList(); }");
        yield return Negative("static List<Order> Run() { using var db = new ShopContext(); IQueryable<Order> Open() { return db.Orders.Where(o => o.Open); } return Open().ToList(); }");
        yield return Negative("static IQueryable<Order> Run(IServiceProvider provider) { var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ShopContext>(); return db.Orders; }");
        yield return Negative("static IQueryable<Order> Run(List<Order> orders) { using var db = new ShopContext(); return orders.AsQueryable(); }");
        yield return Negative("static IQueryable<Order> Run(IQueryable<Order> source) { using var db = new ShopContext(); return source.Where(o => o.Open); }");
        yield return Negative("static IQueryable<Order> Run(ShopContext shared) { using var db = new ShopContext(); return shared.Orders; }");
    }

    [Theory]
    [Trait("Spec", "efd030-query-outlives-context/Using declaration")]
    [Trait("Spec", "efd030-query-outlives-context/Using statement")]
    [Trait("Spec", "efd030-query-outlives-context/Await using in an async method")]
    [Trait("Spec", "efd030-query-outlives-context/Context resolved from a disposed service scope")]
    [Trait("Spec", "efd030-query-outlives-context/Query held in a local or enumerated lazily")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsQueriesReturnedPastTheirContext(string source, string expectedText)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal(expectedText, Text(diagnostic));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "efd030-query-outlives-context/Materialized result")]
    [Trait("Spec", "efd030-query-outlives-context/Context that outlives the method")]
    [Trait("Spec", "efd030-query-outlives-context/Return outside the using scope or in a nested function")]
    [Trait("Spec", "efd030-query-outlives-context/Iterator method")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportQueriesThatRunWhileTheContextLives(string source)
    {
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd030-query-outlives-context/Complete structured finding")]
    public async Task FindingCarriesTheCompleteContract()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource("static IQueryable<Order> Run() { using var db = new ShopContext(); return db.Orders.Where(o => o.Open); }")));

        Assert.Equal(QueryOutlivesContextAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("Reliability", diagnostic.Descriptor.Category);
        Assert.Equal("This query runs after its DbContext 'db' is disposed", diagnostic.GetMessage());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD030", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Contains("DbSet origin 'db.Orders' on context 'db', which is declared by a 'using' declaration", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Equal(QueryOutlivesContextAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("Materialize the result before returning", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("injecting a scoped DbContext", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceScopeEvidenceNamesTheScope()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource("static IQueryable<Order> Run(IServiceProvider provider) { using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ShopContext>(); return db.Orders; }")));

        Assert.Contains("resolved from service scope 'scope'", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd030-query-outlives-context/Suppression")]
    public async Task HonorsPragmaSuppressMessageAndEditorConfig()
    {
        var source = QuerySource("""
            static IQueryable<Order> Pragma()
            {
                using var db = new ShopContext();
            #pragma warning disable EFD030
                return db.Orders;
            #pragma warning restore EFD030
            }

            [SuppressMessage("Reliability", "EFD030", Justification = "Caller materializes inside the scope.")]
            static IQueryable<Order> Attributed() { using var db = new ShopContext(); return db.Orders; }

            static IQueryable<Order> Reported() { using var db = new ShopContext(); return db.Orders.Where(o => o.Open); }
            """);

        Assert.Equal("db.Orders.Where(o => o.Open)", Text(Assert.Single(await AnalyzeAsync(source))));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new QueryOutlivesContextAnalyzer(),
            diagnosticId: QueryOutlivesContextAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + QuerySource("static IQueryable<Order> Run() { using var db = new ShopContext(); return db.Orders; }")));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            analyzer: new QueryOutlivesContextAnalyzer(),
            diagnosticId: QueryOutlivesContextAnalyzer.DiagnosticId);

    private static object[] Case(string member, string expectedText) => [QuerySource(member), expectedText];

    private static object[] Negative(string member) => [QuerySource(member)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    // A minimal stand-in for Microsoft.Extensions.DependencyInjection, so the fixture needs no
    // extra package: the rule matches the method and property names it uses.
    private static string QuerySource(string member) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
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

        interface IServiceScope : IDisposable
        {
            IServiceProvider ServiceProvider { get; }
        }

        static class ServiceProviderExtensions
        {
            public static IServiceScope CreateScope(this IServiceProvider provider) => throw new NotImplementedException();
            public static T GetRequiredService<T>(this IServiceProvider provider) where T : notnull => (T)provider.GetService(typeof(T))!;
            public static T? GetService<T>(this IServiceProvider provider) => (T?)provider.GetService(typeof(T));
        }

        static class Subject
        {
            {{member}}
        }
        """;
}
