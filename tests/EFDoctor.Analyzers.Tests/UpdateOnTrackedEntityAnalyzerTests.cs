using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class UpdateOnTrackedEntityAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static void Run(ShopContext db, int id) { var order = db.Orders.First(o => o.Id == id); order.Status = 2; db.Update(order); db.SaveChanges(); }", "db.Update(order)");
        yield return Case("static void Run(ShopContext db, int id) { var order = db.Orders.Single(o => o.Id == id); order.Status = 2; db.Orders.Update(order); db.SaveChanges(); }", "db.Orders.Update(order)");
        yield return Case("static async Task Run(ShopContext db, int id) { var order = await db.Orders.FindAsync(id); order!.Status = 2; db.Orders.Update(order); await db.SaveChangesAsync(); }", "db.Orders.Update(order)");
        yield return Case("static void Run(ShopContext db, int id) { var order = db.Orders.Find(id); if (order is null) return; order.Status = 2; db.Update(order); db.SaveChanges(); }", "db.Update(order)");
        yield return Case("static async Task Run(ShopContext db, int id) { var order = await db.Orders.Where(o => o.Id == id).FirstOrDefaultAsync(); if (order != null) { order.Status = 2; db.Update(order); } await db.SaveChangesAsync(); }", "db.Update(order)");
        yield return Case("static void Run(ShopContext db, int id) { var order = db.Orders.Include(o => o.Lines).First(o => o.Id == id); db.Update<Order>(order); }", "db.Update<Order>(order)");
        yield return Case("static void Run(ShopContext db, int id) { var order = db.Orders.AsNoTracking().AsTracking().First(o => o.Id == id); db.Update(order); }", "db.Update(order)");
        yield return Case("static void Run(ShopContext db, int id) { var order = db.Set<Order>().Last(o => o.Id == id); order.Status = 3; db.Set<Order>().Update(order); }", "db.Set<Order>().Update(order)");
        yield return Case("static void Run(ShopContext db, int id) { var order = db.Orders.SingleOrDefault(o => o.Id == id)!; Console.WriteLine(order.Id); db.Update(order); }", "db.Update(order)");
        yield return Case("static void Run(ShopContext db, int id) { IQueryable<Order> query = db.Orders; var order = query.First(o => o.Id == id); db.Update(order); }", "db.Update(order)");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static void Run(ShopContext db, Order order) { db.Update(order); db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db, int id) { var order = new Order { Id = id, Status = 2 }; db.Update(order); db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db, int id) { var order = db.Orders.AsNoTracking().First(o => o.Id == id); order.Status = 2; db.Update(order); db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db, int id) { var order = db.Orders.AsNoTrackingWithIdentityResolution().First(o => o.Id == id); db.Update(order); }");
        yield return Negative("static void Run(ShopContext db, ShopContext other, int id) { var order = other.Orders.First(o => o.Id == id); db.Update(order); }");
        yield return Negative("static void Run(ShopContext db, int id) { var order = db.Orders.First(o => o.Id == id); db.ChangeTracker.Clear(); db.Update(order); }");
        yield return Negative("static void Run(ShopContext db, int id) { var order = db.Orders.First(o => o.Id == id); db.Entry(order).State = EntityState.Detached; db.Update(order); }");
        yield return Negative("static void Run(ShopContext db, int id, Order incoming) { var order = db.Orders.First(o => o.Id == id); order = incoming; db.Update(order); }");
        yield return Negative("static void Run(ShopContext db, int id) { var orders = db.Orders.Where(o => o.Id > id).ToList(); db.UpdateRange(orders); }");
        yield return Negative("static void Run(ShopContext db, int id) { var order = db.Orders.First(o => o.Id == id); Action update = () => db.Update(order); update(); }");
        yield return Negative("static void Run(ShopContext db, int id) { var dto = db.Orders.Select(o => new Order { Id = o.Id }).First(o => o.Id == id); db.Update(dto); }");
        yield return Negative("static void Run(Repo repo, int id) { var order = repo.Load(id); repo.Update(order); }");
    }

    [Theory]
    [Trait("Spec", "efd026-update-on-tracked-entity/Update after a tracked query")]
    [Trait("Spec", "efd026-update-on-tracked-entity/Find and DbSet Update")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsUpdateOnATrackedEntity(string source, string expectedText)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal(expectedText, Text(diagnostic));
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "efd026-update-on-tracked-entity/Untracked or external entity")]
    [Trait("Spec", "efd026-update-on-tracked-entity/Tracker cleared or entity detached")]
    [Trait("Spec", "efd026-update-on-tracked-entity/Different context")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportDetachedOrUntrackedEntities(string source)
    {
        Assert.DoesNotContain(AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd026-update-on-tracked-entity/Complete structured finding")]
    public async Task FindingCarriesTheCompleteContract()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source("static void Run(ShopContext db, int id) { var order = db.Orders.First(o => o.Id == id); order.Status = 2; db.Update(order); }")));

        Assert.Equal(UpdateOnTrackedEntityAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("Performance", diagnostic.Descriptor.Category);
        Assert.Equal("'order' is already tracked, so Update is unnecessary and marks every column modified", diagnostic.GetMessage());
        Assert.Equal("advisory", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD026", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal("Local 'order' is loaded with tracking by 'db.Orders.First(o => o.Id == id)' on context 'db', and nothing before this Update clears the tracker or detaches it.", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Equal(UpdateOnTrackedEntityAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("Remove the Update call", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd026-update-on-tracked-entity/Suppression")]
    public async Task HonorsPragmaSuppressMessageAndEditorConfig()
    {
        var source = Source("""
            static void Pragma(ShopContext db, int id)
            {
                var order = db.Orders.First(o => o.Id == id);
            #pragma warning disable EFD026
                db.Update(order);
            #pragma warning restore EFD026
            }

            [SuppressMessage("Performance", "EFD026", Justification = "Every column is written on purpose.")]
            static void Attributed(ShopContext db, int id) { var order = db.Orders.First(o => o.Id == id); db.Update(order); }

            static void Reported(ShopContext db, int id) { var order = db.Orders.First(o => o.Id == id); db.Orders.Update(order); }
            """);

        Assert.Equal("db.Orders.Update(order)", Text(Assert.Single(await AnalyzeAsync(source))));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new UpdateOnTrackedEntityAnalyzer(),
            diagnosticId: UpdateOnTrackedEntityAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + Source("static void Run(ShopContext db, int id) { var order = db.Orders.First(o => o.Id == id); db.Update(order); }")));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new UpdateOnTrackedEntityAnalyzer(),
            diagnosticId: UpdateOnTrackedEntityAnalyzer.DiagnosticId);

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
        }

        sealed class Order
        {
            public int Id { get; set; }
            public int Status { get; set; }
            public List<Line> Lines { get; set; } = new();
        }

        sealed class ShopContext : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();
        }

        sealed class Repo
        {
            public Order Load(int id) => new();
            public void Update(Order order) { }
        }

        static class Subject
        {
            {{member}}
        }
        """;
}
