using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class UnindexedPredicateColumnAnalyzerTests
{
    // Order has a key on Id, and a composite index on (TenantId, Email). Customer extends Party,
    // whose snapshot entity has an index on Code.
    private const string Snapshot = """
        [DbContext(typeof(ShopContext))]
        sealed class ShopContextModelSnapshot : ModelSnapshot
        {
            protected override void BuildModel(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity("Order", b =>
                {
                    b.Property<int>("Id");
                    b.HasKey("Id");
                    b.HasIndex("TenantId", "Email");
                    b.HasAlternateKey("Number");
                });
                modelBuilder.Entity("Party", b =>
                {
                    b.HasKey("Id");
                    b.HasIndex("Code");
                });
                modelBuilder.Entity("Customer", b =>
                {
                    b.HasBaseType("Party");
                });
            }
        }
        """;

    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(ShopContext db, string email) => db.Orders.Where(o => o.Email == email).ToList();", "o.Email", "Order", "Email");
        yield return Case("static object Run(ShopContext db, string email) => db.Orders.Where(o => email == o.Email).ToList();", "o.Email", "Order", "Email");
        yield return Case("static object Run(ShopContext db, DateTime since) => db.Orders.Where(o => o.CreatedUtc >= since).ToList();", "o.CreatedUtc", "Order", "CreatedUtc");
        yield return Case("static object Run(ShopContext db, string prefix) => db.Orders.Where(o => o.Email.StartsWith(prefix)).ToList();", "o.Email", "Order", "Email");
        yield return Case("static object Run(ShopContext db, List<string> numbers) => db.Orders.Where(o => numbers.Contains(o.CustomerNumber)).ToList();", "o.CustomerNumber", "Order", "CustomerNumber");
        yield return Case("static Task<Order?> Run(ShopContext db, string email) => db.Orders.FirstOrDefaultAsync(o => o.Email == email);", "o.Email", "Order", "Email");
        yield return Case("static object Run(ShopContext db, string email, DateTime since) => db.Orders.Where(o => o.Email == email && o.CreatedUtc > since).ToList();", "o.Email", "Order", "Email");
        yield return Case("static object Run(ShopContext db, string email) { var query = db.Orders.AsNoTracking(); return query.Where(o => o.Email == email).ToList(); }", "o.Email", "Order", "Email");
        yield return Case("static object Run(ShopContext db, string email, bool open) { var query = db.Orders.AsQueryable(); if (open) { query = query.Where(o => o.Email == email); } return query.ToList(); }", "o.Email", "Order", "Email");
        yield return Case("static bool Run(ShopContext db, string name) => db.Customers.Any(c => c.Name == name);", "c.Name", "Customer", "Name");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static object? Run(ShopContext db, int id) => db.Orders.FirstOrDefault(o => o.Id == id);");
        yield return Negative("static object Run(ShopContext db, int tenant) => db.Orders.Where(o => o.TenantId == tenant).ToList();");
        yield return Negative("static object Run(ShopContext db, int tenant, string email) => db.Orders.Where(o => o.TenantId == tenant && o.Email == email).ToList();");
        yield return Negative("static object Run(ShopContext db, string number) => db.Orders.Where(o => o.Number == number).ToList();");
        yield return Negative("static object Run(ShopContext db, string code) => db.Customers.Where(c => c.Code == code).ToList();");
        yield return Negative("static object Run(ShopContext db) => db.Orders.Where(o => o.Open).ToList();");
        yield return Negative("static object Run(ShopContext db, bool open) => db.Orders.Where(o => o.Open == open).ToList();");
        yield return Negative("static object Run(ShopContext db, string email, int id) => db.Orders.Where(o => o.Email == email || o.Id == id).ToList();");
        yield return Negative("static object Run(ShopContext db, string email) => db.Orders.Where(o => o.Email.ToLower() == email).ToList();");
        yield return Negative("static object Run(ShopContext db, string term) => db.Orders.Where(o => o.Email.Contains(term)).ToList();");
        yield return Negative("static object Run(ShopContext db) => db.Orders.Where(o => o.Email == o.CustomerNumber).ToList();");
        yield return Negative("static object Run(ShopContext db, string email) => db.Orders.Where(o => o.Customer.Name == email).ToList();");
        yield return Negative("static object Run(ShopContext db, string sku) => db.Lines.Where(l => l.Sku == sku).ToList();");
        yield return Negative("static object Run(List<Order> orders, string email) => orders.Where(o => o.Email == email).ToList();");
        yield return Negative("static object Run(ShopContext db) => db.Orders.OrderBy(o => o.Email).ToList();");
    }

    [Theory]
    [Trait("Spec", "efd036-unindexed-predicate-column/Equality on an unindexed column")]
    [Trait("Spec", "efd036-unindexed-predicate-column/Range, prefix, and membership filters")]
    [Trait("Spec", "efd036-unindexed-predicate-column/Predicate terminal and stored query")]
    [Trait("Spec", "efd036-unindexed-predicate-column/Second column of a composite index")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsFiltersOnUnindexedColumns(string source, string expectedText, string entity, string column)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal(expectedText, Text(diagnostic));
        Assert.Equal($"No index in the model snapshot leads with '{entity}.{column}', which this query filters on", diagnostic.GetMessage());
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "efd036-unindexed-predicate-column/Leading column of an index or key")]
    [Trait("Spec", "efd036-unindexed-predicate-column/Another filter column is covered")]
    [Trait("Spec", "efd036-unindexed-predicate-column/Outside the analyzable shapes")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportIndexedOrUnanalyzableFilters(string source)
    {
        Assert.DoesNotContain(AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd036-unindexed-predicate-column/No snapshot or inconclusive coverage")]
    public async Task DoesNotReportWithoutAConclusiveSnapshot()
    {
        const string Member = "static object Run(ShopContext db, string email) => db.Orders.Where(o => o.Email == email).ToList();";

        Assert.Empty(await AnalyzeAsync(Source(Member, snapshot: "")));
        Assert.Empty(await AnalyzeAsync(Source(Member, snapshot: Snapshot.Replace("b.HasIndex(\"TenantId\", \"Email\");", "b.HasIndex(IndexColumns());\n                    static string[] IndexColumns() => new[] { \"X\" };", StringComparison.Ordinal))));
    }

    [Fact]
    [Trait("Spec", "efd036-unindexed-predicate-column/Complete structured finding")]
    public async Task FindingCarriesTheCompleteContract()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source("static object Run(ShopContext db, string email, DateTime since) => db.Orders.Where(o => o.Email == email && o.CreatedUtc > since).ToList();")));

        Assert.Equal(UnindexedPredicateColumnAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("Performance", diagnostic.Descriptor.Category);
        Assert.Equal("advisory", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD036", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal("The predicate filters 'Order' on [Email, CreatedUtc]. Model snapshot 'ShopContextModelSnapshot' maps 'Order' with indexes and keys leading with [Id, Number, TenantId], so no filter column leads an index.", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Equal(UnindexedPredicateColumnAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("HasIndex", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("small tables, low-selectivity columns", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd036-unindexed-predicate-column/Suppression")]
    public async Task HonorsPragmaSuppressMessageAndEditorConfig()
    {
        var source = Source("""
            static object Pragma(ShopContext db, string email)
            {
            #pragma warning disable EFD036
                return db.Orders.Where(o => o.Email == email).ToList();
            #pragma warning restore EFD036
            }

            [SuppressMessage("Performance", "EFD036", Justification = "Small lookup table.")]
            static object Attributed(ShopContext db, string email) => db.Orders.Where(o => o.Email == email).ToList();

            static object Reported(ShopContext db, DateTime since) => db.Orders.Where(o => o.CreatedUtc > since).ToList();
            """);

        Assert.Equal("o.CreatedUtc", Text(Assert.Single(await AnalyzeAsync(source))));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new UnindexedPredicateColumnAnalyzer(),
            diagnosticId: UnindexedPredicateColumnAnalyzer.DiagnosticId));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new UnindexedPredicateColumnAnalyzer(),
            diagnosticId: UnindexedPredicateColumnAnalyzer.DiagnosticId);

    private static object[] Case(string member, string expectedText, string entity, string column) => [Source(member), expectedText, entity, column];

    private static object[] Negative(string member) => [Source(member)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string Source(string member, string snapshot = Snapshot) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Infrastructure;

        sealed class Order
        {
            public int Id { get; set; }
            public int TenantId { get; set; }
            public string Email { get; set; } = "";
            public string Number { get; set; } = "";
            public string CustomerNumber { get; set; } = "";
            public DateTime CreatedUtc { get; set; }
            public bool Open { get; set; }
            public Customer Customer { get; set; } = new();
        }

        class Party
        {
            public int Id { get; set; }
            public string Code { get; set; } = "";
        }

        sealed class Customer : Party
        {
            public string Name { get; set; } = "";
        }

        sealed class Line
        {
            public int Id { get; set; }
            public string Sku { get; set; } = "";
        }

        sealed class ShopContext : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();
            public DbSet<Customer> Customers => Set<Customer>();
            public DbSet<Line> Lines => Set<Line>();
        }

        {{snapshot}}

        static class Subject
        {
            {{member}}
        }
        """;
}
