using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class UntrackedChangeLostAnalyzerTests
{
    public static IEnumerable<object[]> SingleEntityCases()
    {
        yield return Case("static void Run(ShopContext db, int id) { var order = db.Orders.AsNoTracking().First(o => o.Id == id); order.Status = 2; db.SaveChanges(); }", "order.Status = 2");
        yield return Case("static void Run(ShopContext db, int id) { var order = db.Orders.AsNoTracking().Where(o => o.Id == id).Single(); order.Status = 2; order.Open = false; db.SaveChanges(); }", "order.Status = 2");
        yield return Case("static void Run(ShopContext db, int id) { var order = db.Orders.AsNoTrackingWithIdentityResolution().FirstOrDefault(o => o.Id == id); if (order != null) { order.Status = 2; } db.SaveChanges(); }", "order.Status = 2");
        yield return Case("static void Run(ShopContext db) { var order = db.Set<Order>().AsNoTracking().OrderBy(o => o.Id).Last(); order.Version++; db.SaveChanges(); }", "order.Version++");
        yield return Case("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order.Total += 5; db.SaveChanges(); }", "order.Total += 5");
        yield return Case("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order.Note ??= \"none\"; db.SaveChanges(); }", "order.Note ??= \"none\"");
        yield return Case("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); Console.WriteLine(order.Id); order.Status = order.Status + 1; db.SaveChanges(); }", "order.Status = order.Status + 1");
        yield return Case("static async Task Run(ShopContext db, int id) { var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id); order!.Status = 2; await db.SaveChangesAsync(); }", "order!.Status = 2");
        yield return Case("static async Task Run(ShopContext db, int id) { var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id); order.Open = false; await db.SaveChangesAsync(CancellationToken.None); }", "order.Open = false");
        yield return Case("static void Run(ShopContext db, int id) { var order = db.Orders.AsNoTracking().FirstOrDefault(o => o.Id == id); if (order is null) { return; } order.Open = false; db.SaveChanges(); }", "order.Open = false");
        yield return Case("static void Run(ShopContext db) { IQueryable<Order> query = db.Orders.AsNoTracking(); var order = query.First(); order.Open = false; db.SaveChanges(); }", "order.Open = false");
    }

    public static IEnumerable<object[]> ListCases()
    {
        yield return Case("static void Run(ShopContext db) { var orders = db.Orders.AsNoTracking().Where(o => o.Open).ToList(); foreach (var order in orders) { order.Open = false; } db.SaveChanges(); }", "order.Open = false");
        yield return Case("static async Task Run(ShopContext db) { var orders = await db.Orders.AsNoTracking().ToArrayAsync(); if (orders.Length == 0) { return; } foreach (var order in orders) { order.Version++; } await db.SaveChangesAsync(); }", "order.Version++");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static void Run(ShopContext db) { var order = db.Orders.First(); order.Status = 2; db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().AsTracking().First(); order.Status = 2; db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order.Status = 2; db.Update(order); db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); db.Attach(order); order.Status = 2; db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order.Status = 2; db.Entry(order).State = EntityState.Modified; db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order.Status = 2; db.Orders.Update(order); db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order.Status = 2; Helper(db, order); db.SaveChanges(); }\nstatic void Helper(ShopContext db, Order order) => db.Update(order);");
        yield return Negative("static void Run(ShopContext db, ShopContext other) { var order = db.Orders.AsNoTracking().First(); order.Status = 2; other.SaveChanges(); }");
        yield return Negative("static Order Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order.Status = 2; return order; }");
        yield return Negative("static void Run(ShopContext db) { db.SaveChanges(); var order = db.Orders.AsNoTracking().First(); order.Status = 2; }");
        yield return Negative("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); Console.WriteLine(order.Status); db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order = db.Orders.First(); order.Status = 2; db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); Action change = () => order.Status = 2; change(); db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var orders = db.Orders.AsNoTracking().ToList(); foreach (var order in orders) { order.Open = false; } db.UpdateRange(orders); db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var orders = db.Orders.AsNoTracking().ToList(); foreach (var order in orders) { order.Open = false; db.Update(order); } db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var orders = db.Orders.AsNoTracking().ToList(); orders.ForEach(order => order.Open = false); db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var count = db.Orders.AsNoTracking().Count(); db.SaveChanges(); }");
        yield return Negative("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().Select(o => new Order { Id = o.Id }).First(); order.Status = 2; Console.WriteLine(order); db.SaveChanges(); }");
    }

    [Theory]
    [Trait("Spec", "efd032-untracked-change-lost/Single entity changed and saved")]
    [Trait("Spec", "efd032-untracked-change-lost/Awaited async load and save")]
    [Trait("Spec", "efd032-untracked-change-lost/Compound and increment changes")]
    [MemberData(nameof(SingleEntityCases))]
    public async Task ReportsChangesToAnUntrackedEntity(string source, string expectedText)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal(expectedText, Text(diagnostic));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "efd032-untracked-change-lost/Entities of a list changed in a foreach")]
    [MemberData(nameof(ListCases))]
    public async Task ReportsChangesToUntrackedListElements(string source, string expectedText)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal(expectedText, Text(diagnostic));
        Assert.Contains("Each element 'order'", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Spec", "efd032-untracked-change-lost/Entity attached or passed on")]
    [Trait("Spec", "efd032-untracked-change-lost/Tracked load")]
    [Trait("Spec", "efd032-untracked-change-lost/Save on another context or no save")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportTrackedOrAttachedEntities(string source)
    {
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd032-untracked-change-lost/Complete structured finding")]
    public async Task FindingCarriesTheCompleteContract()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource("""
            static void Run(ShopContext db, int id)
            {
                var order = db.Orders.AsNoTracking().First(o => o.Id == id);
                order.Status = 2;
                db.SaveChanges();
            }
            """)));

        Assert.Equal(UntrackedChangeLostAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("Correctness", diagnostic.Descriptor.Category);
        Assert.Equal("'order' was loaded with AsNoTracking, so SaveChanges does not save this change", diagnostic.GetMessage());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD032", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Contains("Local 'order' is loaded by 'db.Orders.AsNoTracking().First(o => o.Id == id)' with AsNoTracking on context 'db'", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("SaveChanges on the same context on line", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Equal(UntrackedChangeLostAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("ExecuteUpdate", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsOncePerLoadAcrossSeveralSaves()
    {
        var diagnostics = await AnalyzeAsync(QuerySource("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order.Status = 2; db.SaveChanges(); order.Open = false; db.SaveChanges(); }"));

        Assert.Equal("order.Status = 2", Text(Assert.Single(diagnostics)));
    }

    [Fact]
    [Trait("Spec", "efd032-untracked-change-lost/Suppression")]
    public async Task HonorsPragmaSuppressMessageAndEditorConfig()
    {
        var source = QuerySource("""
            static void Pragma(ShopContext db)
            {
                var order = db.Orders.AsNoTracking().First();
            #pragma warning disable EFD032
                order.Status = 2;
            #pragma warning restore EFD032
                db.SaveChanges();
            }

            [SuppressMessage("Correctness", "EFD032", Justification = "An interceptor attaches the entity.")]
            static void Attributed(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order.Status = 2; db.SaveChanges(); }

            static void Reported(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order.Open = false; db.SaveChanges(); }
            """);

        Assert.Equal("order.Open = false", Text(Assert.Single(await AnalyzeAsync(source))));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new UntrackedChangeLostAnalyzer(),
            diagnosticId: UntrackedChangeLostAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + QuerySource("static void Run(ShopContext db) { var order = db.Orders.AsNoTracking().First(); order.Status = 2; db.SaveChanges(); }")));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            analyzer: new UntrackedChangeLostAnalyzer(),
            diagnosticId: UntrackedChangeLostAnalyzer.DiagnosticId);

    private static object[] Case(string member, string expectedText) => [QuerySource(member), expectedText];

    private static object[] Negative(string member) => [QuerySource(member)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string QuerySource(string member) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Order
        {
            public int Id { get; set; }
            public int Status { get; set; }
            public bool Open { get; set; }
            public int Version { get; set; }
            public decimal Total { get; set; }
            public string? Note { get; set; }
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
