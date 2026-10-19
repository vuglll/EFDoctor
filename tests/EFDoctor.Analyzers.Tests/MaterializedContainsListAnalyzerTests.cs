using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class MaterializedContainsListAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(ShopContext db, string region) { var ids = db.Customers.Where(c => c.Region == region).Select(c => c.Id).ToList(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }", "ids.Contains(o.CustomerId)");
        yield return Case("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToArray(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }", "ids.Contains(o.CustomerId)");
        yield return Case("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToHashSet(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }", "ids.Contains(o.CustomerId)");
        yield return Case("static async Task<object> Run(ShopContext db) { var ids = await db.Customers.Where(c => c.Active).Select(c => c.Id).ToListAsync(); return await db.Orders.Where(o => ids.Contains(o.CustomerId)).ToListAsync(); }", "ids.Contains(o.CustomerId)");
        yield return Case("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); return db.Orders.Where(o => Enumerable.Contains(ids, o.CustomerId)).ToList(); }", "Enumerable.Contains(ids, o.CustomerId)");
        yield return Case("static int Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); return db.Orders.Count(o => o.Open && ids.Contains(o.CustomerId)); }", "ids.Contains(o.CustomerId)");
        yield return Case("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); return db.Orders.Where(o => o.Lines.Any(l => ids.Contains(l.ProductId))).ToList(); }", "ids.Contains(l.ProductId)");
        yield return Case("static object Run(ShopContext db, DateTime since) { var ids = db.Customers.Where(c => c.CreatedUtc > since).Select(c => c.Id).ToList(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }", "ids.Contains(o.CustomerId)");
        yield return Case("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); var query = db.Orders.AsNoTracking(); return query.Where(o => ids.Contains(o.CustomerId)).ToList(); }", "ids.Contains(o.CustomerId)");
        yield return Case("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); return db.Orders.FirstOrDefault(o => !ids.Contains(o.CustomerId)); }", "ids.Contains(o.CustomerId)");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).Take(100).ToList(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }");
        yield return Negative("static object Run(ShopContext db, int id) { var ids = db.Customers.Where(c => c.Id == id).Select(c => c.ParentId).ToList(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }");
        yield return Negative("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); if (ids.Count == 0) return new List<Order>(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }");
        yield return Negative("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); Console.WriteLine(ids); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }");
        yield return Negative("static List<int> Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); var orders = db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); return ids; }");
        yield return Negative("static object Run(ShopContext db, List<int> ids) => db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList();");
        yield return Negative("static object Run(ShopContext db) { var ids = new List<int> { 1, 2, 3 }; return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }");
        yield return Negative("static object Run(ShopContext db, ShopContext other) { var ids = other.Customers.Select(c => c.Id).ToList(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }");
        yield return Negative("static object Run(ShopContext db, List<Order> orders) { var ids = db.Customers.Select(c => c.Id).ToList(); return orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }");
        yield return Negative("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); return db.Orders.Where(o => ids.Contains(5)).ToList(); }");
        yield return Negative("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); ids = new List<int>(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }");
        yield return Negative("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); foreach (var id in ids) { Console.WriteLine(id); } return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }");
    }

    [Theory]
    [Trait("Spec", "efd015-materialized-contains-list/Key list from an unbounded query")]
    [Trait("Spec", "efd015-materialized-contains-list/Async load and a HashSet")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsMaterializedKeyListsInContains(string source, string expectedText)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal(expectedText, Text(diagnostic));
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "efd015-materialized-contains-list/Bounded or key-filtered inner query")]
    [Trait("Spec", "efd015-materialized-contains-list/List used for something else")]
    [Trait("Spec", "efd015-materialized-contains-list/In-memory or passed-in list")]
    [Trait("Spec", "efd015-materialized-contains-list/Different contexts")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportBoundedOrReusedLists(string source)
    {
        Assert.DoesNotContain(AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd015-materialized-contains-list/Complete structured finding")]
    public async Task FindingCarriesTheCompleteContract()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }")));

        Assert.Equal(MaterializedContainsListAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("Performance", diagnostic.Descriptor.Category);
        Assert.Equal("'ids' is loaded from another query only to filter this one; compose it as a subquery", diagnostic.GetMessage());
        Assert.Equal("advisory", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD015", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal("Local 'ids' is loaded by 'db.Customers.Select(c => c.Id).ToList()', a query on context 'db' with no Take or key bound, and is used only as the collection of Contains in queries on the same context.", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Equal(MaterializedContainsListAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("IN (SELECT", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd015-materialized-contains-list/Suppression")]
    public async Task HonorsPragmaSuppressMessageAndEditorConfig()
    {
        var source = Source("""
            static object Pragma(ShopContext db)
            {
                var ids = db.Customers.Select(c => c.Id).ToList();
            #pragma warning disable EFD015
                return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList();
            #pragma warning restore EFD015
            }

            [SuppressMessage("Performance", "EFD015", Justification = "Small table.")]
            static object Attributed(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }

            static object Reported(ShopContext db) { var keys = db.Customers.Select(c => c.Id).ToList(); return db.Orders.Where(o => keys.Contains(o.CustomerId)).ToList(); }
            """);

        Assert.Equal("keys.Contains(o.CustomerId)", Text(Assert.Single(await AnalyzeAsync(source))));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new MaterializedContainsListAnalyzer(),
            diagnosticId: MaterializedContainsListAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + Source("static object Run(ShopContext db) { var ids = db.Customers.Select(c => c.Id).ToList(); return db.Orders.Where(o => ids.Contains(o.CustomerId)).ToList(); }")));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new MaterializedContainsListAnalyzer(),
            diagnosticId: MaterializedContainsListAnalyzer.DiagnosticId);

    private static object[] Case(string member, string expectedText) => [Source(member), expectedText];

    private static object[] Negative(string member) => [Source(member)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string Source(string member) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Line
        {
            public int Id { get; set; }
            public int ProductId { get; set; }
        }

        sealed class Customer
        {
            public int Id { get; set; }
            public int ParentId { get; set; }
            public string Region { get; set; } = "";
            public bool Active { get; set; }
            public DateTime CreatedUtc { get; set; }
        }

        sealed class Order
        {
            public int Id { get; set; }
            public int CustomerId { get; set; }
            public bool Open { get; set; }
            public List<Line> Lines { get; set; } = new();
        }

        sealed class ShopContext : DbContext
        {
            public DbSet<Customer> Customers => Set<Customer>();
            public DbSet<Order> Orders => Set<Order>();
        }

        static class Subject
        {
            {{member}}
        }
        """;
}
