using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFDoctor.Analyzers.Tests;

public sealed class MaterializeThenReduceAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(TestContext context) => context.Orders.ToList().First();");
        yield return Case("static object Run(TestContext context) => context.Orders.AsTracking().ToList().First();");
        yield return Case("static int Run(TestContext context) => context.Orders.IgnoreQueryFilters().ToList().Count;");
        yield return Case("static int Run(TestContext context) => context.Orders.ToArray().Count();");
        yield return Case("static int Run(TestContext context) => context.Orders.ToArray().Length;");
        yield return Case("static int Run(TestContext context) => context.Orders.ToList().Count;");
        yield return Case("static async Task<bool> Run(TestContext context) => (await context.Orders.ToListAsync()).Any();");
        yield return Case("static async Task<int> Run(TestContext context) => (await context.Orders.ToListAsync()).Count;");
        yield return Case("static async Task<int> Run(TestContext context) => (await context.Orders.ToArrayAsync()).Length;");
        yield return Case("static object? Run(TestContext context, int id) => context.Orders.ToList().FirstOrDefault(order => order.Id == id);");
        yield return Case("static object Run(TestContext context, int id) => context.Orders.ToList().Single(order => order.Id == id && order.Active);");
        yield return Case("static object? Run(TestContext context) => context.Orders.ToArray().SingleOrDefault();");
        yield return Case("static bool Run(TestContext context) => context.Orders.ToList().All(order => order.Active);");
        yield return Case("static long Run(TestContext context) => context.Orders.ToList().LongCount(order => order.Total > 100);");
        yield return Case("static decimal Run(TestContext context) => context.Orders.Select(order => order.Total).ToList().Sum();");
        yield return Case("static decimal Run(TestContext context) => context.Orders.ToList().Max(order => order.Total);");
        yield return Case("static string? Run(TestContext context) => context.Orders.Select(order => order.Name).ToList().Min();");
        yield return Case("static decimal Run(TestContext context) => context.Orders.ToList().Average(order => order.Total);");
        yield return Case("static object Run(TestContext context) => context.Orders.OrderBy(order => order.Id).ToList().Last();");
        yield return Case("static object? Run(TestContext context) => context.Orders.OrderByDescending(order => order.Id).ToList().LastOrDefault(order => order.Active);");
        yield return Case("static bool Run(TestContext context) => context.Orders.Where(order => order.Active).AsNoTracking().ToList().Any();");
        yield return Case("static object Run(TestContext context) => Enumerable.First(Enumerable.ToList(context.Orders));");
        yield return Case("static int Run(TestContext context) => context.Set<Order>().ToArray().Max(order => order.Id);");
        yield return Case("static int Run(TestContext context) => ((context.Orders.ToList())).Count();");
        yield return Case("static object Run(TestContext context) => new object[] { context.Orders.ToList().First(), context.Orders.ToArray().Length };", expectedCount: 2);
        yield return Case("static object Run(TestContext context) { var query = context.Orders.Where(order => order.Active); return query.ToList().First(); }");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static object Run(TestContext context) => context.Orders.First();");
        yield return Negative("static int Run(TestContext context) => context.Orders.Count();");
        yield return Negative("static async Task<object> Run(TestContext context) => await context.Orders.FirstAsync();");
        yield return Negative("static object Run(TestContext context) => context.Orders.ToList().Last();");
        yield return Negative("static object Run(TestContext context) => context.Orders.ToList().First(order => Compute(order));");
        yield return Negative("static object Run(TestContext context) => context.Orders.ToList().FirstOrDefault(new Order());");
        yield return Negative("static int Run(TestContext context) => context.Orders.ToList().Count(order => order.Name.StartsWith(\"A\"));");
        yield return Negative("static decimal Run(TestContext context) => context.Orders.ToList().Sum(order => order.Total * 2);");
        yield return Negative("static object Run(TestContext context) => context.Orders.ToList().Min()!;");
        yield return Negative("static bool Run(TestContext context) => context.Orders.ToList().Contains(new Order());");
        yield return Negative("static object Run(TestContext context) => context.Orders.ToList().ElementAt(0);");
        yield return Negative("static object Run(TestContext context) => context.Orders.ToList().Where(order => order.Active);");
        yield return Negative("static object Run(TestContext context) => context.Orders.ToList().Select(order => order.Id).First();");
        yield return Negative("static object Run(TestContext context) => context.Orders.AsEnumerable().First();");
        yield return Negative("static object Run(TestContext context) => context.Orders.ToList().ToArray().First();");
        yield return Negative("static object Run(TestContext context) { var orders = context.Orders.ToList(); return orders.First(); }");
        yield return Negative("static object Run(IQueryable<Order> query) => query.ToList().First();");
        yield return Negative("static object Run() => new[] { new Order() }.AsQueryable().ToList().First();");
        yield return Negative("static object Run(List<Order> orders) => orders.ToList().First();");
        yield return Negative("static object Run(TestContext context) => context.Orders.ToListAsync().Result.First();");
        yield return Negative("static object Run(Store store) => store.ToList().First();", "sealed class Store { public Store ToList() => this; public object First() => new(); }");
        yield return Negative("static object Run(TestContext context) => missing.ToList().First();");
        yield return Negative("static string Run() => \"context.Orders.ToList().First()\";");
        yield return new object[] { "// <auto-generated/>\n" + Source("static object Run(TestContext context) => context.Orders.ToList().First();") };
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsMaterializeThenReduce(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal(MaterializeThenReduceAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
            Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
            Assert.Equal("EFD019", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
            Assert.Contains("DbSet origin", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
            Assert.Contains("immediately reduced by", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
            Assert.Contains("buffers every row", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact], StringComparison.Ordinal);
            Assert.Contains("Keep the materialized list only when its rows are also needed elsewhere", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public void PositiveCasesCompileWithoutErrors(string source, int expectedCount)
    {
        _ = expectedCount;
        var errors = AnalyzerTestHarness.GetCompilationDiagnostics(source)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.Empty(errors);
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportQuerySideStoredBoundaryOrUnrelatedShapes(string source)
    {
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Theory]
    [InlineData("context.Orders.ToList().First()", "context.Orders.ToList()", "'Enumerable.First'", "a single element", "Apply 'First' to the query")]
    [InlineData("context.Orders.ToArray().Length", "context.Orders.ToArray()", "the array 'Length' property", "a count", "Apply 'Count' to the query")]
    [InlineData("context.Orders.ToList().Count", "context.Orders.ToList()", "the 'List<T>.Count' property", "a count", "Apply 'Count' to the query")]
    [InlineData("context.Orders.ToList().Any(order => order.Active)", "context.Orders.ToList()", "'Enumerable.Any'", "a boolean", "'AnyAsync'")]
    [InlineData("context.Orders.ToList().Sum(order => order.Total)", "context.Orders.ToList()", "'Enumerable.Sum'", "an aggregate value", "Apply 'Sum' to the query")]
    public async Task ReportsOnMaterializerWithReducerEvidence(string expression, string expectedSpan, string expectedReducer, string expectedResult, string expectedRemediation)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source($"static object Run(TestContext context) => {expression};")));

        Assert.Equal(expectedSpan, Text(diagnostic));
        Assert.Contains($"immediately reduced by {expectedReducer}", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("'context.Orders'", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains($"to {expectedResult};", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact], StringComparison.Ordinal);
        Assert.Contains(expectedRemediation, diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains(expectedReducer, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("context.Orders.ToList().Count > 0", "count > 0", "Apply 'Any' to the query")]
    [InlineData("0 < context.Orders.ToList().Count", "count > 0", "Apply 'Any' to the query")]
    [InlineData("context.Orders.ToList().Count != 0", "count != 0", "Apply 'Any' to the query")]
    [InlineData("context.Orders.ToList().Count() >= 1", "count >= 1", "Apply 'Any' to the query")]
    [InlineData("context.Orders.ToList().Count(order => order.Active) > 0", "count > 0", "Apply 'Any' to the query")]
    [InlineData("context.Orders.ToArray().Length == 0", "count == 0", "Apply the negation of 'Any' to the query")]
    [InlineData("context.Orders.ToList().Count <= 0", "count <= 0", "Apply the negation of 'Any' to the query")]
    [InlineData("context.Orders.ToList().LongCount() < 1", "count < 1", "Apply the negation of 'Any' to the query")]
    [InlineData("(context.Orders.ToList().Count) > 0 && context.Orders.Any()", "count > 0", "Apply 'Any' to the query")]
    public async Task CountComparedForExistenceRecommendsAny(string expression, string comparison, string expectedRemediation)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source($"static bool Run(TestContext context) => {expression};")));

        var remediation = diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]!;
        Assert.StartsWith(expectedRemediation, remediation, StringComparison.Ordinal);
        Assert.DoesNotContain("'Count'", remediation, StringComparison.Ordinal);
        Assert.Contains("keep any predicate as the Any predicate", remediation, StringComparison.Ordinal);
        Assert.Contains($"The count is compared as '{comparison}', which only tests existence.", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("to a boolean;", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(await context.Orders.ToListAsync()).Count > 0", "Replace the awaited materialization with the EF Core asynchronous reducer 'AnyAsync'")]
    [InlineData("(await context.Orders.ToArrayAsync()).Length == 0", "Replace the awaited materialization with the negation of the EF Core asynchronous reducer 'AnyAsync'")]
    public async Task AwaitedCountComparedForExistenceRecommendsAnyAsync(string expression, string expectedRemediation)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source($"static async Task<bool> Run(TestContext context) => {expression};")));

        Assert.StartsWith(expectedRemediation, diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("context.Orders.ToList().Count == 5")]
    [InlineData("context.Orders.ToList().Count > 1")]
    [InlineData("context.Orders.ToList().Count >= 0")]
    [InlineData("context.Orders.ToList().Count > limit")]
    [InlineData("context.Orders.ToList().Count + 1 > 0")]
    public async Task CountUsedAsAValueStillRecommendsCount(string expression)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source($"static bool Run(TestContext context, int limit) => {expression};")));

        Assert.StartsWith("Apply 'Count' to the query", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.DoesNotContain("only tests existence", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> StoredCountCases()
    {
        // The examples from the issue that asked for this.
        yield return new object[] { "static bool Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Count > 0; }", "Apply 'Any' to the query", "'orders.Count > 0'" };
        yield return new object[] { "static bool Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Count >= 1; }", "Apply 'Any' to the query", "'orders.Count >= 1'" };
        yield return new object[] { "static bool Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Count == 0; }", "Apply 'Any' to the query", "'orders.Count == 0'" };
        yield return new object[] { "static bool Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Count <= 0; }", "Apply 'Any' to the query", "'orders.Count <= 0'" };
        yield return new object[] { "static bool Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Count < 1; }", "Apply 'Any' to the query", "'orders.Count < 1'" };
        yield return new object[] { "static int Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Count; }", "Apply 'Count' to the query", "'orders.Count'" };

        yield return new object[] { "static bool Run(TestContext context) { var orders = context.Orders.Where(order => order.Active).ToList(); return orders.Any(); }", "Apply 'Any' to the query", "'orders.Any()'" };
        yield return new object[] { "static bool Run(TestContext context) { var orders = context.Orders.ToList(); return !orders.Any(); }", "Apply 'Any' to the query", "'orders.Any()'" };
        yield return new object[] { "static bool Run(TestContext context) { var orders = context.Orders.ToArray(); if (orders.Length == 0) { return false; } return true; }", "Apply 'Any' to the query", "'orders.Length == 0'" };
        yield return new object[] { "static long Run(TestContext context) { var orders = context.Orders.ToArray(); return orders.LongCount() + orders.Count(); }", "Apply 'Count' to the query", "'orders.LongCount()', 'orders.Count()'" };
        yield return new object[] { "static int Run(TestContext context) { IEnumerable<Order> orders = context.Orders.ToList(); return (orders).Count(); }", "Apply 'Count' to the query", "'(orders).Count()'" };
        yield return new object[] { "static async Task<bool> Run(TestContext context) { var orders = await context.Orders.ToListAsync(); return orders.Count > 0; }", "Replace the awaited materialization with the EF Core asynchronous reducer 'AnyAsync'", "'orders.Count > 0'" };
        yield return new object[] { "static async Task<int> Run(TestContext context) { var orders = await context.Orders.ToArrayAsync(); return orders.Length; }", "Replace the awaited materialization with the EF Core asynchronous reducer 'CountAsync'", "'orders.Length'" };
        yield return new object[] { "static string Run(TestContext context) { var orders = context.Orders.ToList(); return $\"{orders.Count} orders\"; }", "Apply 'Count' to the query", "'orders.Count'" };
    }

    [Theory]
    [MemberData(nameof(StoredCountCases))]
    public async Task ReportsStoredResultUsedOnlyForItsCount(string member, string expectedRemediation, string expectedReads)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source(member)));

        Assert.EndsWith(member.Contains("Async()", StringComparison.Ordinal) ? "Async()" : member.Contains("ToArray()", StringComparison.Ordinal) ? "ToArray()" : "ToList()", Text(diagnostic), StringComparison.Ordinal);
        Assert.Equal("This EF Core query is fully materialized and then used only for its count through local 'orders'", diagnostic.GetMessage());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.StartsWith(expectedRemediation, diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains($"stored in local 'orders', which this method uses only for its count: {expectedReads}.", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        var testsForEmpty = expectedReads.Contains("== 0", StringComparison.Ordinal) || expectedReads.Contains("<= 0", StringComparison.Ordinal) || expectedReads.Contains("< 1", StringComparison.Ordinal);
        Assert.Equal(testsForEmpty, diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]!.Contains("Negate it where the code tests for an empty result.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeveralCountReadsRecommendOneQuery()
    {
        var source = Source("""
            static int Run(TestContext context)
            {
                var orders = context.Orders.ToList();
                if (orders.Count == 0)
                {
                    return -1;
                }

                return orders.Count;
            }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        var remediation = diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]!;
        Assert.StartsWith("Apply 'Count' to the query", remediation, StringComparison.Ordinal);
        Assert.Contains("The local is read 2 times, so run the query-side operator once and reuse its value.", remediation, StringComparison.Ordinal);
        Assert.Contains("'orders.Count == 0', 'orders.Count'", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); return orders.First(); }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); Console.WriteLine(orders.Count); return orders; }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); foreach (var order in orders) { Console.WriteLine(order.Id); } return orders.Count; }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Count > 0 ? orders[0] : null!; }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); Use(orders); return orders.Count; } static void Use(object value) { }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); var copy = orders; return orders.Count; }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Count(order => order.Active); }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Any(order => order.Active); }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Sum(order => order.Total) + orders.Count; }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); Func<int> count = () => orders.Count; return count(); }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); int Count() => orders.Count; return Count(); }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); orders = new List<Order>(); return orders.Count; }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); orders.Clear(); return orders.Count; }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Capacity; }")]
    [InlineData("static void Run(TestContext context) { var orders = context.Orders.ToList(); }")]
    [InlineData("static object Run(TestContext context) { List<Order> orders; orders = context.Orders.ToList(); return orders.Count; }")]
    [InlineData("static object Run(TestContext context) { var orders = context.Orders.AsEnumerable().ToList(); return orders.Count; }")]
    [InlineData("static object Run(List<Order> source) { var orders = source.ToList(); return orders.Count; }")]
    [InlineData("static object Run(TestContext context) { var count = context.Orders.Count(); return count > 0; }")]
    public async Task DoesNotReportStoredResultWhoseRowsMayBeUsed(string member)
    {
        Assert.Empty(await AnalyzeAsync(Source(member)));
    }

    [Fact]
    public async Task UnboundedMaterializationYieldsToStoredCountFinding()
    {
        var source = Source("static bool Run(TestContext context) { var orders = context.Orders.ToList(); return orders.Count > 0; }");

        var diagnostics = await AnalyzeWithAsync(source, new MaterializeThenReduceAnalyzer(), new UnboundedQueryMaterializationAnalyzer());

        Assert.Equal(MaterializeThenReduceAnalyzer.DiagnosticId, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task AwaitedMaterializerRecommendsAsyncReducer()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source("static async Task<object> Run(TestContext context) => (await context.Orders.ToListAsync()).First();")));

        Assert.Equal("context.Orders.ToListAsync()", Text(diagnostic));
        Assert.Contains("Replace the awaited materialization with the EF Core asynchronous reducer 'FirstAsync'", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticAndReducedSyntaxProduceEquivalentEvidence()
    {
        var reduced = Assert.Single(await AnalyzeAsync(Source("static object Run(TestContext context) => context.Orders.ToList().First();")));
        var @static = Assert.Single(await AnalyzeAsync(Source("static object Run(TestContext context) => Enumerable.First(Enumerable.ToList(context.Orders));")));

        Assert.Equal(reduced.Properties[DiagnosticPropertyNames.Evidence], @static.Properties[DiagnosticPropertyNames.Evidence]);
    }

    [Theory]
    [InlineData("context.Orders.ToList().First()", 0, 1)]
    [InlineData("(await context.Orders.ToListAsync()).Count", 0, 1)]
    [InlineData("context.Orders.ToList().First(order => Compute(order))", 1, 0)]
    [InlineData("context.Orders.ToList().Last()", 1, 0)]
    public async Task Efd005YieldsOnlyWhereEfd019Reports(string expression, int expectedEfd005, int expectedEfd019)
    {
        var source = Source($"static async Task<object> Run(TestContext context) => {expression};");

        var diagnostics = await AnalyzeWithAsync(source, new UnboundedQueryMaterializationAnalyzer(), new MaterializeThenReduceAnalyzer());

        Assert.Equal(expectedEfd005, diagnostics.Count(static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId));
        Assert.Equal(expectedEfd019, diagnostics.Count(static diagnostic => diagnostic.Id == MaterializeThenReduceAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task HonorsPragmaSuppressionWhileSiblingStillReports()
    {
        var source = Source("""
            #pragma warning disable EFD019 // Reviewed: the table holds a handful of configuration rows.
            static object Suppressed(TestContext context) => context.Orders.ToList().First();
            #pragma warning restore EFD019
            static object Reported(TestContext context) => context.Orders.ToList().First();
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HonorsEditorConfigSuppression()
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            Source("static object Run(TestContext context) => context.Orders.ToList().First();"),
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new MaterializeThenReduceAnalyzer(),
            diagnosticId: MaterializeThenReduceAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task HonorsSuppressMessageAttributeWhileSiblingStillReports()
    {
        var source = Source("""
            [SuppressMessage("Performance", "EFD019", Justification = "The table holds a handful of configuration rows.")]
            static object Suppressed(TestContext context) => context.Orders.ToList().First();
            static object Reported(TestContext context) => context.Orders.ToList().First();
            """);

        Assert.Single(await AnalyzeAsync(source));
    }

    [Fact]
    public void DescriptorUsesPerformanceWarningMetadata()
    {
        Assert.Equal("EFD019", MaterializeThenReduceAnalyzer.Rule.Id);
        Assert.Equal("Performance", MaterializeThenReduceAnalyzer.Rule.Category);
        Assert.Equal(DiagnosticSeverity.Warning, MaterializeThenReduceAnalyzer.Rule.DefaultSeverity);
        Assert.True(MaterializeThenReduceAnalyzer.Rule.IsEnabledByDefault);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            analyzer: new MaterializeThenReduceAnalyzer(),
            diagnosticId: MaterializeThenReduceAnalyzer.DiagnosticId);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeWithAsync(string source, params DiagnosticAnalyzer[] analyzers)
    {
        var compilation = AnalyzerTestHarness.CreateCompilation(source, true, false, false, false, false, null, MaterializeThenReduceAnalyzer.DiagnosticId);
        return await compilation.WithAnalyzers(ImmutableArray.Create(analyzers)).GetAnalyzerDiagnosticsAsync();
    }

    private static object[] Case(string member, string additionalTypes = "", int expectedCount = 1) => [Source(member, additionalTypes), expectedCount];

    private static object[] Negative(string member, string additionalTypes = "") => [Source(member, additionalTypes)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string Source(string member, string additionalTypes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Order
        {
            public int Id { get; set; }
            public bool Active { get; set; }
            public decimal Total { get; set; }
            public string Name { get; set; } = "";
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();
        }

        {{additionalTypes}}

        static class Subject
        {
            {{member}}

            static bool Compute(Order order) => order.Id % 2 == 0;
        }
        """;
}
