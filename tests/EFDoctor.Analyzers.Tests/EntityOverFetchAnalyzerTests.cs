using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using EFDoctor.Analyzers;

namespace EFDoctor.Analyzers.Tests;

public sealed class EntityOverFetchAnalyzerTests
{
    // Order has eight counted properties: Id and CreatedUtc from its base type, and CustomerId,
    // Total, Status, Notes, ExternalId, and Kind.
    public static IEnumerable<object[]> ProjectionCases()
    {
        yield return Case("static async Task<object> Run(TestContext db, int id) { var orders = await db.Orders.Where(o => o.CustomerId == id).ToListAsync(); return orders.Select(o => new Summary(o.Id, o.Total)).ToList(); }");
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => new { o.Id, o.Total }).ToList(); }");
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.AsNoTracking().OrderBy(o => o.Id).ToList(); return orders.Select(o => o.Status.Length).Sum(); }");
        yield return Case("static object Run(TestContext db) { var orders = db.Set<Order>().Where(o => o.Total > 0).ToList(); return Enumerable.Select(orders, o => o.Id).ToArray(); }");
        yield return Case("static object Run(TestContext db) { IEnumerable<Order> orders = db.Orders.ToList(); return orders.Select(o => (o.Id, o.Total)).ToList(); }");
        yield return Case("static object Run(TestContext db) { List<Order> orders = db.Orders.ToList(); return (orders).Select(o => o.Total * 2).ToList(); }");
    }

    public static IEnumerable<object[]> ForEachCases()
    {
        yield return Case("static decimal Run(TestContext db) { var orders = db.Orders.ToList(); var sum = 0m; foreach (var o in orders) { sum += o.Total; Console.WriteLine(o.Status); } return sum; }");
        yield return Case("static async Task Run(TestContext db) { var orders = await db.Orders.ToListAsync(); foreach (Order order in orders) { if (order.Total > 10) { Console.WriteLine(order.Id); } } }");
        yield return Case("static void Run(TestContext db) { var orders = db.Orders.ToArray(); foreach (var o in orders) { Action print = () => Console.WriteLine(o.Id); print(); } }");
    }

    public static IEnumerable<object[]> MaterializerCases()
    {
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => o.Id).ToList(); }");
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToArray(); return orders.Select(o => o.Id).ToList(); }");
        yield return Case("static async Task<object> Run(TestContext db) { var orders = await db.Orders.ToArrayAsync(); return orders.Select(o => o.Id).ToList(); }");
        yield return Case("static async Task<object> Run(TestContext db, CancellationToken token) { var orders = await db.Orders.ToListAsync(token).ConfigureAwait(false); return orders.Count; }", expectedCount: 0);
    }

    public static IEnumerable<object[]> ReducerCases()
    {
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Any(o => o.Total > 100); }");
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.All(o => o.Status == \"paid\"); }");
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Count(o => o.Total > 100); }");
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Sum(o => o.Total); }");
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Min(o => o.CreatedUtc); }");
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Max(o => o.Total); }");
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Average(o => o.Total); }");
    }

    public static IEnumerable<object[]> IndexCases()
    {
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders[0].Total; }");
        yield return Case("static object Run(TestContext db) { var orders = db.Orders.ToArray(); return orders[0].Total + orders[orders.Length - 1].Total; }");
    }

    public static IEnumerable<object[]> PositiveCases() =>
        ProjectionCases().Concat(ForEachCases()).Concat(MaterializerCases().Where(static fixture => (int)fixture[1] == 1)).Concat(ReducerCases()).Concat(IndexCases());

    public static IEnumerable<object[]> EscapeCases()
    {
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); Console.WriteLine(orders.Select(o => o.Id).Count()); return orders; }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); Helper.Use(orders); return orders.Select(o => o.Id).ToList(); }");
        yield return Silent("static void Run(TestContext db) { var orders = db.Orders.ToList(); foreach (var o in orders) { Console.WriteLine(o.Id); Helper.Use(o); } }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => o).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => new { o.Id, Order = o }).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Max(o => o); }");
        yield return Silent("static object Run(TestContext db, Order other) { var orders = db.Orders.ToList(); return orders.Any(o => o == other || o.Id == 1); }");
    }

    public static IEnumerable<object[]> StoredCases()
    {
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); var copy = orders; return orders.Select(o => o.Id).ToList(); }");
        yield return Silent("static void Run(TestContext db) { var orders = db.Orders.ToList(); Helper.Cache = orders; foreach (var o in orders) { Console.WriteLine(o.Id); } }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); var kept = new List<Order>(); foreach (var o in orders) { if (o.Total > 0) { kept.Add(o); } } return kept.Count; }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); Order? last = null; foreach (var o in orders) { last = o; Console.WriteLine(o.Id); } return last?.Id ?? 0; }");
    }

    public static IEnumerable<object[]> ModifiedCases()
    {
        yield return Silent("static void Run(TestContext db) { var orders = db.Orders.ToList(); foreach (var o in orders) { o.Status = \"done\"; } db.SaveChanges(); }");
        yield return Silent("static void Run(TestContext db) { var orders = db.Orders.ToList(); foreach (var o in orders) { o.Total += 1; } db.SaveChanges(); }");
        yield return Silent("static void Run(TestContext db) { var orders = db.Orders.ToList(); foreach (var o in orders) { o.CustomerId++; } db.SaveChanges(); }");
        yield return Silent("static void Run(TestContext db) { var orders = db.Orders.ToList(); orders[0].Status = \"done\"; db.SaveChanges(); }");
        yield return Silent("static void Run(TestContext db) { var orders = db.Orders.ToList(); foreach (var o in orders) { (o.Status, o.Notes) = (\"done\", null); } db.SaveChanges(); }");
    }

    public static IEnumerable<object[]> UncountedMemberCases()
    {
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => o.Customer!.Name).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Sum(o => o.Lines.Count); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => new { o.Id, o.Label }).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => o.Display).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => o.Sequence).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => o.ToString()).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => o.Describe() + o.Id).ToList(); }");
    }

    public static IEnumerable<object[]> UnsupportedUseCases()
    {
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Where(o => o.Total > 0).Select(o => o.Id).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.OrderBy(o => o.Total).Select(o => o.Id).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.First().Id; }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select((o, index) => o.Id + index).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); orders = new List<Order>(); return orders.Select(o => o.Id).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.ToDictionary(o => o.Id); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); var first = orders[0]; return first.Id; }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(Helper.Describe).ToList(); }");
    }

    public static IEnumerable<object[]> ShapedQueryCases()
    {
        yield return Silent("static object Run(TestContext db) { var totals = db.Orders.Select(o => o.Total).ToList(); return totals.Sum(t => t); }");
        yield return Silent("static object Run(TestContext db) { var rows = db.Orders.Select(o => new { o.Id, o.Total }).ToList(); return rows.Select(r => r.Id).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.Include(o => o.Customer).ToList(); return orders.Select(o => o.Id).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.Include(o => o.Lines).ThenInclude(l => l.Product).ToList(); return orders.Select(o => o.Id).ToList(); }");
    }

    public static IEnumerable<object[]> UnprovenCases()
    {
        yield return Silent("static object Run(IEnumerable<Order> source) { var orders = source.ToList(); return orders.Select(o => o.Id).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = Helper.Load(); return orders.Select(o => o.Id).ToList(); }");
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.AsEnumerable().Where(o => o.Total > 0).ToList(); return orders.Select(o => o.Id).ToList(); }");
        yield return Silent("static object Run(TestContext db) => db.Orders.ToList().Select(o => o.Id).ToList();");
        yield return Silent("static object Run(TestContext db) { var order = db.Orders.First(); return order.Id; }");
        yield return Silent("static object Run(TestContext db) { Helper.Cache = db.Orders.ToList(); return Helper.Cache.Select(o => o.Id).ToList(); }");
    }

    public static IEnumerable<object[]> ThresholdNegativeCases()
    {
        // Five of eight read: more than half.
        yield return Silent("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => new { o.Id, o.Total, o.Status, o.Notes, o.Kind }).ToList(); }");
        // One of four read: fewer than four unread.
        yield return Silent("static object Run(TestContext db) { var tags = db.Tags.ToList(); return tags.Select(t => t.Name).ToList(); }");
        // Two of five read: three unread.
        yield return Silent("static object Run(TestContext db) { var lines = db.Lines.ToList(); return lines.Select(l => new { l.Id, l.Quantity }).ToList(); }");
    }

    public static IEnumerable<object[]> NegativeCases() =>
        EscapeCases().Concat(StoredCases()).Concat(ModifiedCases()).Concat(UncountedMemberCases()).Concat(UnsupportedUseCases()).Concat(ShapedQueryCases()).Concat(UnprovenCases()).Concat(ThresholdNegativeCases());

    [Theory]
    [MemberData(nameof(ProjectionCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Result projected in memory")]
    public Task ReportsResultProjectedInMemory(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(ForEachCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Result read in a foreach loop")]
    public Task ReportsResultReadInForEach(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(MaterializerCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Synchronous and array materializers")]
    public Task ReportsEveryMaterializer(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(ReducerCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Lambda reducers")]
    public Task ReportsLambdaReducers(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(IndexCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Element read by index")]
    public Task ReportsElementReadByIndex(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Fact]
    [Trait("Spec", "efd037-entity-over-fetch/Several uses combined")]
    public async Task CombinesReadsAcrossUsesIntoOneFinding()
    {
        var source = QuerySource("""
            static object Run(TestContext db)
            {
                var orders = db.Orders.ToList();
                if (orders.Count == 0 || !orders.Any())
                {
                    return 0;
                }

                foreach (var order in orders)
                {
                    Console.WriteLine(order.Status);
                }

                return orders.Select(o => o.Id).Count() + orders.Count(o => o.Total > 0);
            }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Contains("reads 3 of the 8 scalar properties of 'Order': Id, Status, Total.", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Spec", "efd037-entity-over-fetch/Query composed through a local")]
    [InlineData("static object Run(TestContext db) { var query = db.Orders.Where(o => o.Total > 0); var orders = query.ToList(); return orders.Select(o => o.Id).ToList(); }")]
    [InlineData("static object Run(TestContext db) { IQueryable<Order> query = db.Orders; query = query.OrderBy(o => o.Id); var orders = query.ToList(); return orders.Select(o => o.Id).ToList(); }")]
    public async Task FollowsAQueryHeldInALocal(string member)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource(member)));

        Assert.Equal("query.ToList()", Text(diagnostic));
    }

    [Theory]
    [Trait("Spec", "efd037-entity-over-fetch/Threshold met")]
    [InlineData("o.Id", 1)]
    [InlineData("new { o.Id, o.Total }", 2)]
    [InlineData("new { o.Id, o.Total, o.Status, o.Kind }", 4)]
    public async Task ReportsWhenAtMostHalfAreReadAndFourAreUnread(string projection, int read)
    {
        var source = QuerySource($"static object Run(TestContext db) {{ var orders = db.Orders.ToList(); return orders.Select(o => {projection}).ToList(); }}");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal($"This query loads complete 'Order' entities, but the method reads only {read} of their 8 scalar properties", diagnostic.GetMessage());
    }

    [Fact]
    [Trait("Spec", "efd037-entity-over-fetch/More than half read")]
    public Task DoesNotReportWhenMoreThanHalfAreRead() =>
        AssertCountAsync((string)ThresholdNegativeCases().ElementAt(0)[0], 0);

    [Theory]
    [Trait("Spec", "efd037-entity-over-fetch/Fewer than four unread")]
    [InlineData(1)]
    [InlineData(2)]
    public Task DoesNotReportSmallEntities(int index) =>
        AssertCountAsync((string)ThresholdNegativeCases().ElementAt(index)[0], 0);

    [Theory]
    [Trait("Spec", "efd037-entity-over-fetch/No property read")]
    [InlineData("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Count; }")]
    [InlineData("static object Run(TestContext db) { var orders = db.Orders.ToArray(); return orders.Length + orders.Count(); }")]
    [InlineData("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Any(); }")]
    [InlineData("static void Run(TestContext db) { var orders = db.Orders.ToList(); }")]
    public async Task DoesNotReportWhenNoPropertyIsRead(string member)
    {
        Assert.Empty(await AnalyzeAsync(QuerySource(member)));
    }

    [Fact]
    [Trait("Spec", "efd037-entity-over-fetch/Inherited properties are counted")]
    public async Task CountsAndReadsInheritedProperties()
    {
        var source = QuerySource("static object Run(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => o.CreatedUtc).ToList(); }");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Contains("reads 1 of the 8 scalar properties of 'Order': CreatedUtc.", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd037-entity-over-fetch/Navigations and unmapped members are not counted")]
    public void CountsOnlyMappedScalarAutoProperties()
    {
        var compilation = AnalyzerTestHarness.CreateCompilation(QuerySource(string.Empty), true, true, false, false, false, null, EntityOverFetchAnalyzer.DiagnosticId);
        var order = compilation.GetTypeByMetadataName("Order")!;

        var counted = EntityOverFetchAnalyzer.CountedProperties(order).Select(static property => property.Name).OrderBy(static name => name, StringComparer.Ordinal);

        Assert.Equal(["CreatedUtc", "CustomerId", "ExternalId", "Id", "Kind", "Notes", "Status", "Total"], counted);
    }

    [Theory]
    [MemberData(nameof(EscapeCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Entity returned or passed on")]
    public Task DoesNotReportWhenAnEntityEscapes(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(StoredCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Entity stored")]
    public Task DoesNotReportWhenAnEntityIsStored(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(ModifiedCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Entity modified")]
    public Task DoesNotReportWhenAnEntityIsModified(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(UncountedMemberCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Navigation or uncounted member used")]
    public Task DoesNotReportWhenAnUncountedMemberIsUsed(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(UnsupportedUseCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Unsupported use of the local")]
    public Task DoesNotReportUnsupportedUses(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(ShapedQueryCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Query already shaped")]
    public Task DoesNotReportProjectedOrIncludedQueries(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Theory]
    [MemberData(nameof(UnprovenCases))]
    [Trait("Spec", "efd037-entity-over-fetch/Unproven source")]
    public Task DoesNotReportUnprovenSources(string source, int expectedCount) => AssertCountAsync(source, expectedCount);

    [Fact]
    [Trait("Spec", "efd037-entity-over-fetch/Unproven source")]
    public async Task DoesNotReportWithoutEntityFrameworkReference()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            sealed class Row { public int A { get; set; } public int B { get; set; } public int C { get; set; } public int D { get; set; } public int E { get; set; } }
            static class Subject
            {
                static object Run(IEnumerable<Row> rows) { var list = rows.ToList(); return list.Select(r => r.A).ToList(); }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeEntityFramework: false,
            analyzer: new EntityOverFetchAnalyzer(),
            diagnosticId: EntityOverFetchAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd037-entity-over-fetch/Complete structured finding")]
    [Trait("Spec", "efd037-entity-over-fetch/Diagnostic location")]
    public async Task ReportsMaterializerWithCompleteAdvisoryProperties()
    {
        var source = QuerySource("static async Task<object> Run(TestContext db, int id) { var orders = await db.Orders.Where(o => o.CustomerId == id).ToListAsync(); return orders.Select(o => new Summary(o.Id, o.Total)).ToList(); }");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("db.Orders.Where(o => o.CustomerId == id).ToListAsync()", Text(diagnostic));
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal("This query loads complete 'Order' entities, but the method reads only 2 of their 8 scalar properties", diagnostic.GetMessage());
        Assert.Equal("advisory", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD037", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal(EntityOverFetchAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("actual cost depends", EntityOverFetchAnalyzer.Impact, StringComparison.Ordinal);

        var evidence = diagnostic.Properties[DiagnosticPropertyNames.Evidence]!;
        Assert.Contains("DbSet origin 'db.Orders'", evidence, StringComparison.Ordinal);
        Assert.Contains("materialized into local 'orders'", evidence, StringComparison.Ordinal);
        Assert.Contains("reads 2 of the 8 scalar properties of 'Order': Id, Total.", evidence, StringComparison.Ordinal);

        var remediation = diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]!;
        Assert.Contains("Select(x => new { x.Id, x.Total })", remediation, StringComparison.Ordinal);
        Assert.Contains("Keep the entity load when", remediation, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd037-entity-over-fetch/Standard suppression")]
    public async Task HonorsPragmaAndSuppressMessageWhileSiblingStillReports()
    {
        var source = QuerySource("""
            #pragma warning disable EFD037 // Reviewed: the table is narrow in production.
            static object Pragma(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => o.Id).ToList(); }
            #pragma warning restore EFD037
            [SuppressMessage("Performance", "EFD037", Justification = "The table is narrow in production.")]
            static object Attribute(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => o.Id).ToList(); }
            static object Reported(TestContext db) { var orders = db.Orders.ToList(); return orders.Select(o => o.Id).ToList(); }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Contains("Reported", LineOf(diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd037-entity-over-fetch/Standard suppression")]
    public async Task HonorsEditorConfigSuppression()
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            (string)MaterializerCases().First()[0],
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new EntityOverFetchAnalyzer(),
            diagnosticId: EntityOverFetchAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd037-entity-over-fetch/Generated code")]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + (string)MaterializerCases().First()[0]));
    }

    [Fact]
    public void FixturesCompile()
    {
        var failures = PositiveCases().Concat(NegativeCases()).Concat(MaterializerCases())
            .Select(static fixture => (string)fixture[0])
            .SelectMany(static source => AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => $"{diagnostic.Id} {diagnostic.GetMessage()} in {source[source.IndexOf("static class Subject", StringComparison.Ordinal)..]}"))
            .ToArray();

        Assert.Empty(failures);
        Assert.True(PositiveCases().Count() >= 10);
        Assert.True(NegativeCases().Count() >= 10);
    }

    [Fact]
    public void DescriptorUsesAdvisoryPerformanceMetadata()
    {
        Assert.Equal("EFD037", EntityOverFetchAnalyzer.Rule.Id);
        Assert.Equal("Performance", EntityOverFetchAnalyzer.Rule.Category);
        Assert.Equal(DiagnosticSeverity.Info, EntityOverFetchAnalyzer.Rule.DefaultSeverity);
        Assert.Equal("advisory", EntityOverFetchAnalyzer.Confidence);
        Assert.True(EntityOverFetchAnalyzer.Rule.IsEnabledByDefault);
    }

    private static async Task AssertCountAsync(string source, int expectedCount)
    {
        var matches = await AnalyzeAsync(source);
        Assert.Equal(expectedCount, matches.Count(static diagnostic => diagnostic.Id == EntityOverFetchAnalyzer.DiagnosticId));
    }

    private static string Text(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string LineOf(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString();

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new EntityOverFetchAnalyzer(),
            diagnosticId: EntityOverFetchAnalyzer.DiagnosticId);

    private static object[] Case(string member, int expectedCount = 1) => [QuerySource(member), expectedCount];

    private static object[] Silent(string member) => [QuerySource(member), 0];

    private static string QuerySource(string member) => $$"""
        using System;
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations.Schema;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        enum OrderKind { Standard, Express }

        abstract class EntityBase
        {
            public int Id { get; set; }
            public DateTime CreatedUtc { get; set; }
        }

        sealed class Order : EntityBase
        {
            public int CustomerId { get; set; }
            public decimal Total { get; set; }
            public string Status { get; set; } = "";
            public string? Notes { get; set; }
            public Guid ExternalId { get; set; }
            public OrderKind Kind { get; set; }
            public Customer? Customer { get; set; }
            public List<OrderLine> Lines { get; set; } = new();
            [NotMapped]
            public string Label { get; set; } = "";
            public string Display => Status + Id;
            public int Sequence { get; }
            public static int Instances { get; set; }
            internal int Internal { get; set; }
            public string Describe() => Status;
        }

        sealed class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        sealed class OrderLine
        {
            public int Id { get; set; }
            public int OrderId { get; set; }
            public int Quantity { get; set; }
            public decimal Price { get; set; }
            public string Sku { get; set; } = "";
            public Product? Product { get; set; }
        }

        sealed class Product
        {
            public int Id { get; set; }
        }

        sealed class Tag
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Color { get; set; } = "";
            public int Rank { get; set; }
        }

        sealed record Summary(int Id, decimal Total);

        sealed class TestContext : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();
            public DbSet<OrderLine> Lines => Set<OrderLine>();
            public DbSet<Tag> Tags => Set<Tag>();
        }

        static class Helper
        {
            public static List<Order> Cache = new();
            public static void Use(object value) { }
            public static List<Order> Load() => new();
            public static string Describe(Order order) => order.Status;
        }

        static class Subject
        {
            {{member}}
        }
        """;
}
