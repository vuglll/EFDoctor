using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using EFDoctor.Analyzers;

namespace EFDoctor.Analyzers.Tests;

public sealed class StaleTrackedEntitiesAnalyzerTests
{
    private const string Update = "ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, 1m))";

    public static IEnumerable<object[]> FindCases()
    {
        yield return Case($"static async Task Run(ShopContext db, int id) {{ var product = await db.Products.FindAsync(id); await db.Products.Where(p => p.Id == id).{Update}; }}");
        yield return Case("static void Run(ShopContext db, int id) { var product = db.Products.Find(id); db.Products.Where(p => p.Id == id).ExecuteUpdate(s => s.SetProperty(p => p.Price, 1m)); }");
        yield return Case($"static async Task Run(ShopContext db, int id) {{ var product = await db.Set<Product>().FindAsync(id); await db.Set<Product>().{Update}; }}");
    }

    public static IEnumerable<object[]> TerminalCases()
    {
        foreach (var terminal in new[] { "First()", "FirstOrDefault()", "Single(p => p.Id == 1)", "SingleOrDefault()", "OrderBy(p => p.Id).Last()", "OrderBy(p => p.Id).LastOrDefault()", "ToList()", "ToArray()" })
        {
            yield return Case($"static void Run(ShopContext db) {{ var loaded = db.Products.Where(p => p.Price > 0).{terminal}; db.Products.ExecuteDelete(); }}");
        }

        foreach (var terminal in new[] { "FirstAsync()", "FirstOrDefaultAsync()", "SingleAsync()", "SingleOrDefaultAsync()", "ToListAsync()", "ToArrayAsync()" })
        {
            yield return Case($"static async Task Run(ShopContext db) {{ var loaded = await db.Products.Where(p => p.Price > 0).{terminal}; await db.Products.{Update}; }}");
        }

        yield return Case($"static async Task Run(ShopContext db) {{ var loaded = await db.Products.AsTracking().Include(p => p.Category).ToListAsync(); await db.Products.{Update}; }}");
        yield return Case("static void Run(ShopContext db) { var query = db.Products.Where(p => p.Price > 0); var loaded = query.ToList(); query.ExecuteDelete(); }");
    }

    public static IEnumerable<object[]> DeleteCases()
    {
        yield return Case("static void Run(ShopContext db, int id) { var product = db.Products.Find(id); db.Products.Where(p => p.Id == id).ExecuteDelete(); }");
        yield return Case("static async Task Run(ShopContext db, int id) { var product = await db.Products.FirstAsync(p => p.Id == id); await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(); }");
    }

    public static IEnumerable<object[]> NestedCases()
    {
        yield return Case("static void Run(ShopContext db, int id, bool purge) { var product = db.Products.Find(id); if (purge) { db.Products.Where(p => p.Id == id).ExecuteDelete(); } }");
        yield return Case("static void Run(ShopContext db, int[] ids) { var products = db.Products.ToList(); foreach (var id in ids) { db.Products.Where(p => p.Id == id).ExecuteDelete(); } }");
        yield return Case("static void Run(ShopContext db, int id) { var product = db.Products.Find(id); try { db.Products.Where(p => p.Id == id).ExecuteDelete(); } catch (InvalidOperationException) { } }");
        yield return Case("static void Run(ShopContext db, int id) { var product = db.Products.Find(id); Console.WriteLine(id); { var count = db.Products.Where(p => p.Id == id).ExecuteDelete(); Console.WriteLine(count); } }");
        yield return Case("static int Run(ShopContext db, int id) { var product = db.Products.Find(id); return db.Products.Where(p => p.Id == id).ExecuteDelete(); }");
    }

    public static IEnumerable<object[]> MemberContextCases()
    {
        yield return Case("static void Run() { }", "sealed class Service { private readonly ShopContext _db = null!; void Run(int id) { var product = _db.Products.Find(id); _db.Products.Where(p => p.Id == id).ExecuteDelete(); } }");
        yield return Case("static void Run() { }", "sealed class Service { private ShopContext Db { get; } = null!; void Run(int id) { var product = Db.Products.Find(id); this.Db.Products.Where(p => p.Id == id).ExecuteDelete(); } }");
        yield return Case("static void Run() { }", "sealed class DerivedContext : DbContext { public DbSet<Product> Products => Set<Product>(); void Run(int id) { var product = Products.Find(id); Set<Product>().Where(p => p.Id == id).ExecuteDelete(); } }");
        yield return Case("static void Run() { }", "sealed class Service(ShopContext db) { void Run(int id) { var product = db.Products.Find(id); db.Products.Where(p => p.Id == id).ExecuteDelete(); } }");
    }

    public static IEnumerable<object[]> PositiveCases() =>
        FindCases().Concat(TerminalCases()).Concat(DeleteCases()).Concat(NestedCases()).Concat(MemberContextCases());

    public static IEnumerable<object[]> NoTrackingCases()
    {
        yield return Silent("static void Run(ShopContext db) { var products = db.Products.AsNoTracking().ToList(); db.Products.ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db, int id) { var product = db.Products.AsNoTrackingWithIdentityResolution().First(p => p.Id == id); db.Products.ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db) { var products = db.Products.Where(p => p.Price > 0).AsNoTracking().OrderBy(p => p.Id).ToArray(); db.Products.ExecuteDelete(); }");
    }

    public static IEnumerable<object[]> OtherShapeCases()
    {
        yield return Silent("static void Run(ShopContext db, int id) { var category = db.Categories.Find(id); db.Products.Where(p => p.CategoryId == id).ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db) { var names = db.Products.Select(p => p.Name).ToList(); db.Products.ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db) { var first = db.Products.Select(p => new { p.Id, p.Name }).First(); db.Products.ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db) { var count = db.Products.Count(); var any = db.Products.Any(); db.Products.ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db) { var categories = db.Products.Select(p => p.Category).ToList(); db.Products.ExecuteDelete(); }");
    }

    public static IEnumerable<object[]> ContextCases()
    {
        yield return Silent("static void Run(ShopContext first, ShopContext second, int id) { var product = first.Products.Find(id); second.Products.Where(p => p.Id == id).ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db, Func<ShopContext> factory, int id) { var product = db.Products.Find(id); using var other = factory(); other.Products.Where(p => p.Id == id).ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db, ShopContext other, int id) { var product = db.Products.Find(id); db = other; db.Products.Where(p => p.Id == id).ExecuteDelete(); }");
        yield return Silent("static void Run(Func<ShopContext> factory, int id) { var product = factory().Products.Find(id); factory().Products.Where(p => p.Id == id).ExecuteDelete(); }");
        yield return Silent("static void Run(Holder holder, int id) { var product = holder.Db.Products.Find(id); holder.Db.Products.Where(p => p.Id == id).ExecuteDelete(); }");
    }

    public static IEnumerable<object[]> OrderCases()
    {
        yield return Silent("static void Run(ShopContext db, int id) { db.Products.Where(p => p.Id == id).ExecuteDelete(); var product = db.Products.Find(id); }");
        yield return Silent("static void Run(ShopContext db, int id, bool purge) { if (purge) { db.Products.Where(p => p.Id == id).ExecuteDelete(); } else { var product = db.Products.Find(id); Console.WriteLine(product); } }");
        yield return Silent("static void Run(ShopContext db, int id) { { var product = db.Products.Find(id); Console.WriteLine(product); } db.Products.Where(p => p.Id == id).ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db, int id) { var product = db.Products.Find(id); Action purge = () => db.Products.Where(p => p.Id == id).ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db, int id) { var product = db.Products.Find(id); void Purge() { db.Products.Where(p => p.Id == id).ExecuteDelete(); } }");
        yield return Silent("static void Run(ShopContext db, int id) { Func<Product?> load = () => { var product = db.Products.Find(id); return product; }; db.Products.Where(p => p.Id == id).ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db, int id) { Product? product; product = db.Products.Find(id); db.Products.Where(p => p.Id == id).ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db, int id) { db.Products.Find(id)!.Price = 2m; db.Products.Where(p => p.Id == id).ExecuteDelete(); }");
    }

    public static IEnumerable<object[]> ManagedTrackerCases()
    {
        yield return Silent($"static async Task Run(ShopContext db, int id) {{ var product = await db.Products.FindAsync(id); await db.Products.{Update}; db.ChangeTracker.Clear(); }}");
        yield return Silent($"static async Task Run(ShopContext db, int id) {{ var product = await db.Products.FindAsync(id); db.ChangeTracker.Clear(); await db.Products.{Update}; }}");
        yield return Silent($"static async Task<Product?> Run(ShopContext db, int id) {{ var product = await db.Products.FindAsync(id); await db.Products.{Update}; db.Entry(product!).Reload(); return product; }}");
        yield return Silent($"static async Task<Product?> Run(ShopContext db, int id) {{ var product = await db.Products.FindAsync(id); await db.Products.{Update}; await db.Entry(product!).ReloadAsync(); return product; }}");
        yield return Silent("static void Run(ShopContext db, int id) { var product = db.Products.Find(id); db.Products.Where(p => p.Id == id).ExecuteDelete(); db.Entry(product!).State = EntityState.Detached; }");
        yield return Silent("static void Run(ShopContext db) { var products = db.Products.ToList(); db.Products.ExecuteDelete(); foreach (var product in products) { db.Entry(product).State = EntityState.Detached; } }");
    }

    public static IEnumerable<object[]> BulkAloneCases()
    {
        yield return Silent("static void Run(ShopContext db, int id) { db.Products.Where(p => p.Id == id).ExecuteDelete(); }");
        yield return Silent($"static async Task Run(ShopContext db) {{ await db.Products.{Update}; await db.SaveChangesAsync(); }}");
        yield return Silent("static void Run(ShopContext db, int id) { var product = db.Products.Find(id); Bulk.ExecuteDelete(db.Products); }");
        yield return Silent("static void Run(ShopContext db, List<Product> cached) { var products = cached.ToList(); db.Products.ExecuteDelete(); }");
    }

    public static IEnumerable<object[]> NegativeCases() =>
        NoTrackingCases().Concat(OtherShapeCases()).Concat(ContextCases()).Concat(OrderCases()).Concat(ManagedTrackerCases()).Concat(BulkAloneCases()).Concat(DisjointFilterCases());

    [Theory]
    [MemberData(nameof(FindCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/Find then ExecuteUpdate")]
    public Task ReportsFindThenExecuteUpdate(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(TerminalCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/Query terminals as loads")]
    public Task ReportsQueryTerminalLoads(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(DeleteCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/ExecuteDelete")]
    public Task ReportsExecuteDelete(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(NestedCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/Bulk operation nested in a later statement")]
    public Task ReportsNestedBulkOperations(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(MemberContextCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/Context held in a field or property")]
    public Task ReportsMemberContexts(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(NoTrackingCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/No-tracking load")]
    public Task DoesNotReportNoTrackingLoads(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(OtherShapeCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/Different entity type or projection")]
    public Task DoesNotReportOtherTypesOrProjections(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(ContextCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/Different or unprovable context")]
    public Task DoesNotReportUnprovenContexts(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(OrderCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/Order not proven")]
    public Task DoesNotReportUnprovenOrder(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(ManagedTrackerCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/Tracker cleared, or entity reloaded or detached")]
    public Task DoesNotReportManagedTracker(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(BulkAloneCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/Bulk operation alone")]
    public Task DoesNotReportBulkOperationAlone(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Fact]
    [Trait("Spec", "efd038-stale-tracked-entities/Loaded entity read afterwards")]
    [Trait("Spec", "efd038-stale-tracked-entities/Complete structured finding")]
    [Trait("Spec", "efd038-stale-tracked-entities/Diagnostic location")]
    public async Task ReadAfterwardsIsHighConfidenceWithCompleteProperties()
    {
        var source = QuerySource($$"""
            static async Task<decimal> Run(ShopContext db, int id)
            {
                var product = await db.Products.FirstAsync(p => p.Id == id);
                await db.Products.Where(p => p.Id == id).{{Update}};
                return product.Price;
            }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal($"db.Products.Where(p => p.Id == id).{Update}", Text(diagnostic));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("This ExecuteUpdateAsync bypasses the change tracker, so the 'Product' entities already tracked through 'product' are stale", diagnostic.GetMessage());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD038", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal(StaleTrackedEntitiesAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Equal(StaleTrackedEntitiesAnalyzer.Remediation, diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]);
        Assert.Contains("bypass the change tracker", StaleTrackedEntitiesAnalyzer.Impact, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Clear()", StaleTrackedEntitiesAnalyzer.Remediation, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", StaleTrackedEntitiesAnalyzer.Remediation, StringComparison.Ordinal);
        Assert.Contains("filter cannot match the loaded entities", StaleTrackedEntitiesAnalyzer.Remediation, StringComparison.Ordinal);

        var evidence = diagnostic.Properties[DiagnosticPropertyNames.Evidence]!;
        Assert.Contains("Local 'product' is loaded with tracking by 'db.Products.FirstAsync(p => p.Id == id)' on context 'db'.", evidence, StringComparison.Ordinal);
        Assert.Contains("ExecuteUpdateAsync then updates 'Product' rows directly in the database", evidence, StringComparison.Ordinal);
        Assert.Contains("The method then uses 'product' again on line", evidence, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Spec", "efd038-stale-tracked-entities/Same type loaded again")]
    [InlineData("await db.Products.FindAsync(id)")]
    [InlineData("db.Products.Find(id)")]
    [InlineData("await db.Products.Where(p => p.Id == id).SingleAsync()")]
    [InlineData("db.Set<Product>().ToList()")]
    public async Task LoadingTheTypeAgainIsHighConfidence(string reload)
    {
        var source = QuerySource($"static async Task<object?> Run(ShopContext db, int id) {{ var product = await db.Products.FindAsync(id); await db.Products.{Update}; return {reload}; }}");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains("The method then loads 'Product' again with", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("returns the instances the context already tracks", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Spec", "efd038-stale-tracked-entities/Modified entity saved afterwards")]
    [InlineData("var products = await db.Products.ToListAsync(); foreach (var product in products) { product.Price = 0m; }", "await db.SaveChangesAsync();", "SaveChangesAsync")]
    [InlineData("var products = await db.Products.ToArrayAsync(); products[0].Price += 1m;", "db.SaveChanges();", "SaveChanges")]
    [InlineData("var products = await db.Products.FirstAsync(); products.Name = \"renamed\";", "db.SaveChanges();", "SaveChanges")]
    public async Task SavingAModifiedEntityAfterwardsIsHighConfidence(string loadAndModify, string save, string method)
    {
        var source = QuerySource($"static async Task Run(ShopContext db) {{ {loadAndModify} await db.Products.ExecuteDeleteAsync(); {save} }}");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains($"The method modifies 'products' and calls {method} on the same context", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("ExecuteDeleteAsync then deletes 'Product' rows", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> DisjointFilterCases()
    {
        yield return Silent("static void Run(ShopContext db) { var product = db.Products.Where(p => p.Name == \"a\").FirstOrDefault(); db.Products.Where(p => p.Name == \"b\").ExecuteDelete(); db.SaveChanges(); }");
        yield return Silent("static void Run(ShopContext db) { var product = db.Products.FirstOrDefault(p => p.CategoryId == 1 && p.Price > 0); db.Products.Where(p => p.Price < 5 && 2 == p.CategoryId).ExecuteDelete(); }");
        yield return Silent("static void Run(ShopContext db) { const string kept = \"a\"; var products = db.Products.Where(p => p.Name == kept).ToList(); db.Products.Where(p => p.Price > 0).Where(p => p.Name == \"b\").ExecuteDelete(); }");
    }

    [Theory]
    [MemberData(nameof(DisjointFilterCases))]
    [Trait("Spec", "efd038-stale-tracked-entities/Filters that cannot overlap")]
    public Task DoesNotReportFiltersThatCannotOverlap(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [Trait("Spec", "efd038-stale-tracked-entities/Filters that cannot overlap")]
    [InlineData("p => p.Name == \"a\"", "p => p.Name == \"a\"")]
    [InlineData("p => p.Name == \"a\"", "p => p.Name == name")]
    [InlineData("p => p.Name == \"a\"", "p => p.Name != \"b\"")]
    [InlineData("p => p.Name == \"a\"", "p => p.Name == \"b\" || p.Price > 0")]
    [InlineData("p => p.Name == \"a\"", "p => p.CategoryId == 2")]
    public async Task ReportsFiltersThatMayOverlap(string loadFilter, string bulkFilter)
    {
        var source = QuerySource($"static void Run(ShopContext db, string name) {{ var product = db.Products.Where({loadFilter}).FirstOrDefault(); db.Products.Where({bulkFilter}).ExecuteDelete(); }}");

        Assert.Single(await AnalyzeAsync(source));
    }

    [Theory]
    [Trait("Spec", "efd038-stale-tracked-entities/Left stale")]
    [InlineData("")]
    [InlineData("var category = db.Categories.Find(id); Console.WriteLine(category);")]
    [InlineData("var names = db.Products.AsNoTracking().Select(p => p.Name).ToList(); Console.WriteLine(names.Count);")]
    [InlineData("other.SaveChanges();")]
    [InlineData("db.SaveChanges();")]
    [InlineData("db.Categories.Add(new Category()); db.SaveChanges();")]
    public async Task UnusedStaleEntitiesAreMediumConfidence(string after)
    {
        var source = QuerySource($"static void Run(ShopContext db, ShopContext other, int id) {{ var product = db.Products.Find(id); Console.WriteLine(product); db.Products.Where(p => p.Id == id).ExecuteDelete(); {after} }}");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("medium", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("Nothing later in this method uses 'product', loads 'Product' again, or saves a change to it", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd038-stale-tracked-entities/One finding per bulk operation")]
    public async Task ReportsOncePerBulkOperationNamingTheClosestLoad()
    {
        var source = QuerySource("""
            static void Run(ShopContext db, int id)
            {
                var first = db.Products.Find(id);
                var second = db.Products.Where(p => p.Price > 0).ToList();
                db.Products.Where(p => p.Id == id).ExecuteDelete();
                db.Products.Where(p => p.Price < 0).ExecuteDelete();
            }
            """);

        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, static diagnostic => Assert.Contains("Local 'second' is loaded with tracking", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Spec", "efd038-stale-tracked-entities/Standard suppression")]
    public async Task HonorsPragmaAndSuppressMessageWhileSiblingStillReports()
    {
        var source = QuerySource("""
            static void Pragma(ShopContext db, int id)
            {
                var product = db.Products.Find(id);
            #pragma warning disable EFD038 // Reviewed: the cleanup can't match the loaded product.
                db.Products.Where(p => p.Price < 0).ExecuteDelete();
            #pragma warning restore EFD038
            }

            [SuppressMessage("Correctness", "EFD038", Justification = "The cleanup can't match the loaded product.")]
            static void Attribute(ShopContext db, int id) { var product = db.Products.Find(id); db.Products.Where(p => p.Price < 0).ExecuteDelete(); }

            static void Reported(ShopContext db, int id) { var product = db.Products.Find(id); db.Products.Where(p => p.Id == id).ExecuteDelete(); }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Contains("Reported", LineOf(diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd038-stale-tracked-entities/Standard suppression")]
    public async Task HonorsEditorConfigSuppression()
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            (string)DeleteCases().First()[0],
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new StaleTrackedEntitiesAnalyzer(),
            diagnosticId: StaleTrackedEntitiesAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd038-stale-tracked-entities/Generated code")]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + (string)DeleteCases().First()[0]));
    }

    [Fact]
    public void FixturesCompile()
    {
        var failures = PositiveCases().Concat(NegativeCases())
            .Select(static fixture => (string)fixture[0])
            .SelectMany(static source => AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => $"{diagnostic.Id} {diagnostic.GetMessage()} at {diagnostic.Location.GetLineSpan().StartLinePosition} in {source[source.IndexOf("sealed class Holder", StringComparison.Ordinal)..]}"))
            .ToArray();

        Assert.Empty(failures);
        Assert.True(PositiveCases().Count() >= 10);
        Assert.True(NegativeCases().Count() >= 10);
    }

    [Fact]
    public void DescriptorUsesCorrectnessWarningMetadata()
    {
        Assert.Equal("EFD038", StaleTrackedEntitiesAnalyzer.Rule.Id);
        Assert.Equal("Correctness", StaleTrackedEntitiesAnalyzer.Rule.Category);
        Assert.Equal(DiagnosticSeverity.Warning, StaleTrackedEntitiesAnalyzer.Rule.DefaultSeverity);
        Assert.True(StaleTrackedEntitiesAnalyzer.Rule.IsEnabledByDefault);
    }

    private static async Task AssertCountAsync(string source, int expectedCount)
    {
        var matches = await AnalyzeAsync(source);
        Assert.Equal(expectedCount, matches.Count(static diagnostic => diagnostic.Id == StaleTrackedEntitiesAnalyzer.DiagnosticId));
    }

    private static string Text(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string LineOf(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString();

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new StaleTrackedEntitiesAnalyzer(),
            diagnosticId: StaleTrackedEntitiesAnalyzer.DiagnosticId);

    private static object[] Case(string member, string additionalTypes = "") => [QuerySource(member, additionalTypes), 1];

    private static object[] Silent(string member) => [QuerySource(member), 0];

    private static string QuerySource(string member, string additionalTypes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Product
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Price { get; set; }
            public int CategoryId { get; set; }
            public Category? Category { get; set; }
        }

        sealed class Category
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        sealed class ShopContext : DbContext
        {
            public DbSet<Product> Products => Set<Product>();
            public DbSet<Category> Categories => Set<Category>();
        }

        sealed class Holder
        {
            public ShopContext Db { get; } = null!;
        }

        static class Bulk
        {
            public static int ExecuteDelete(IQueryable<Product> source) => 0;
        }

        {{additionalTypes}}

        static class Subject
        {
            {{member}}
        }
        """;
}
