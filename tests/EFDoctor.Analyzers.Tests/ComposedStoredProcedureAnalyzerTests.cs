using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class ComposedStoredProcedureAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(ShopContext db, int id) => db.Orders.FromSqlRaw(\"EXEC dbo.GetOrders @p0\", id).Where(o => o.Open).ToList();", "Where", "EXEC");
        yield return Case("static object Run(ShopContext db) => db.Orders.FromSqlRaw(\"  exec GetOrders\").OrderBy(o => o.Id).ToList();", "OrderBy", "exec");
        yield return Case("static object? Run(ShopContext db, int id) => db.Orders.FromSql($\"EXECUTE dbo.GetOrder {id}\").FirstOrDefault();", "FirstOrDefault", "EXECUTE");
        yield return Case("static Task<int> Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXEC dbo.GetOrders\").CountAsync();", "CountAsync", "EXEC");
        yield return Case("static object Run(ShopContext db, int id) => db.Orders.FromSqlInterpolated($\"EXEC dbo.GetOrders {id}\").Select(o => o.Id).ToList();", "Select", "EXEC");
        yield return Case("static object Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXEC GetOrders\").AsNoTracking().TagWith(\"report\").Include(o => o.Lines).ToList();", "Include", "EXEC");
        yield return Case("static object Run(ShopContext db) { var orders = db.Orders.FromSqlRaw(\"CALL get_orders()\"); return orders.OrderBy(o => o.Id).ToList(); }", "OrderBy", "CALL");
        yield return Case("static bool Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXEC GetOrders\").IgnoreQueryFilters().Any();", "Any", "EXEC");
        yield return Case("static object Run(ShopContext db) => RelationalQueryableExtensions.FromSqlRaw(db.Orders, \"EXEC GetOrders\").Skip(10).ToList();", "Skip", "EXEC");
        yield return Case("static Task<Order?> Run(ShopContext db, int id) => db.Orders.FromSql($\"EXEC dbo.GetOrder {id}\").SingleOrDefaultAsync();", "SingleOrDefaultAsync", "EXEC");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static object Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXEC dbo.GetOrders\").ToList();");
        yield return Negative("static Task<List<Order>> Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXEC dbo.GetOrders\").ToListAsync();");
        yield return Negative("static object Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXEC dbo.GetOrders\").AsNoTracking().ToArray();");
        yield return Negative("static object Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXEC dbo.GetOrders\").AsEnumerable().Where(o => o.Open).ToList();");
        yield return Negative("static object Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXEC dbo.GetOrders\").ToList().Where(o => o.Open).ToList();");
        yield return Negative("static void Run(ShopContext db) { foreach (var order in db.Orders.FromSqlRaw(\"EXEC dbo.GetOrders\")) { Console.WriteLine(order.Id); } }");
        yield return Negative("static object Run(ShopContext db, int id) => db.Orders.FromSqlRaw(\"SELECT * FROM dbo.GetOrdersFn({0})\", id).Where(o => o.Open).ToList();");
        yield return Negative("static object Run(ShopContext db) => db.Orders.FromSqlRaw(\"SELECT * FROM Orders WHERE Note = 'EXEC'\").Where(o => o.Open).ToList();");
        yield return Negative("static object Run(ShopContext db, string sql) => db.Orders.FromSqlRaw(sql).Where(o => o.Open).ToList();");
        yield return Negative("static object Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXECUTIONS_VIEW\").Where(o => o.Open).ToList();");
        yield return Negative("static object Run(ShopContext db) => db.Orders.Where(o => o.Open).ToList();");
        yield return Negative("static Task Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXEC GetOrders\").LoadAsync();");
        yield return Negative("static object Run(ShopContext db, bool open) { var orders = db.Orders.FromSqlRaw(\"EXEC GetOrders\"); if (open) { orders = db.Orders.Where(o => o.Open); } return orders.OrderBy(o => o.Id).ToList(); }");
    }

    [Theory]
    [Trait("Spec", "efd031-composed-stored-procedure/Where over EXEC")]
    [Trait("Spec", "efd031-composed-stored-procedure/Terminal that composes")]
    [Trait("Spec", "efd031-composed-stored-procedure/Include and pass-through operators")]
    [Trait("Spec", "efd031-composed-stored-procedure/CALL syntax and a query local")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsCompositionOverAProcedureCall(string source, string operatorName, string keyword)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.StartsWith($"EF Core cannot compose {operatorName} over the stored-procedure call in FromSql", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains($"begins with '{keyword}'", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "efd031-composed-stored-procedure/Direct materialization")]
    [Trait("Spec", "efd031-composed-stored-procedure/Composable SQL")]
    [Trait("Spec", "efd031-composed-stored-procedure/Unknown SQL text")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportMaterializedOrComposableSql(string source)
    {
        Assert.DoesNotContain(AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd031-composed-stored-procedure/Complete structured finding")]
    public async Task FindingCarriesTheCompleteContract()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source("static object Run(ShopContext db, int id) => db.Orders.FromSqlRaw(\"EXEC dbo.GetOrders @p0\", id).Where(o => o.Open).ToList();")));

        Assert.Equal(ComposedStoredProcedureAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("Correctness", diagnostic.Descriptor.Category);
        Assert.Equal("EF Core cannot compose Where over the stored-procedure call in FromSqlRaw", diagnostic.GetMessage());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD031", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal("'Where' composes over 'FromSqlRaw', whose SQL begins with 'EXEC': a stored-procedure call, which EF Core cannot wrap in a subquery.", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Equal(ComposedStoredProcedureAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("table-valued function", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Equal("db.Orders.FromSqlRaw(\"EXEC dbo.GetOrders @p0\", id).Where(o => o.Open)", Text(diagnostic));
    }

    [Fact]
    public async Task ReportsOnlyTheFirstComposingOperator()
    {
        var diagnostics = await AnalyzeAsync(Source("static object Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXEC GetOrders\").Where(o => o.Open).OrderBy(o => o.Id).Take(5).ToList();"));

        Assert.Equal("EF Core cannot compose Where over the stored-procedure call in FromSqlRaw", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    [Trait("Spec", "efd031-composed-stored-procedure/Suppression")]
    public async Task HonorsPragmaSuppressMessageAndEditorConfig()
    {
        var source = Source("""
            static object Pragma(ShopContext db)
            {
            #pragma warning disable EFD031
                return db.Orders.FromSqlRaw("EXEC GetOrders").Where(o => o.Open).ToList();
            #pragma warning restore EFD031
            }

            [SuppressMessage("Correctness", "EFD031", Justification = "The provider composes procedure calls.")]
            static object Attributed(ShopContext db) => db.Orders.FromSqlRaw("EXEC GetOrders").Where(o => o.Open).ToList();

            static object Reported(ShopContext db) => db.Orders.FromSqlRaw("EXEC GetOrders").OrderBy(o => o.Id).ToList();
            """);

        Assert.Single(await AnalyzeAsync(source));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new ComposedStoredProcedureAnalyzer(),
            diagnosticId: ComposedStoredProcedureAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + Source("static object Run(ShopContext db) => db.Orders.FromSqlRaw(\"EXEC GetOrders\").Where(o => o.Open).ToList();")));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new ComposedStoredProcedureAnalyzer(),
            diagnosticId: ComposedStoredProcedureAnalyzer.DiagnosticId);

    private static object[] Case(string member, string operatorName, string keyword) => [Source(member), operatorName, keyword];

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
            public bool Open { get; set; }
            public List<Line> Lines { get; set; } = new();
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
