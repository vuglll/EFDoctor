using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class SaveChangesInLoopAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("for (var i = 0; i < 1; i++) { context.SaveChanges(); }");
        yield return Case("foreach (var i in new[] { 1 }) { context.SaveChanges(); }");
        yield return Case("while (DateTime.UtcNow.Ticks < 0) { context.SaveChanges(); }");
        yield return Case("do { context.SaveChanges(); } while (DateTime.UtcNow.Ticks < 0);");
        yield return Case("for (var i = 0; i < 1; i++) { context.SaveChanges(acceptAllChangesOnSuccess: false); }");
        yield return AsyncCase("foreach (var i in new[] { 1 }) { await context.SaveChangesAsync(); }");
        yield return AsyncCase("while (DateTime.UtcNow.Ticks < 0) { await context.SaveChangesAsync(token); }");
        yield return AsyncCase("await foreach (var i in Stream()) { await context.SaveChangesAsync(token); }");
        yield return Case("foreach (var i in new[] { 1 }) { if (i > 0) { context.SaveChanges(); } }");
        yield return Case("foreach (var i in new[] { 1 }) { try { context.SaveChanges(); } finally { } }");
        yield return Case("foreach (var i in new[] { 1 }) { using var stream = new System.IO.MemoryStream(); context.SaveChanges(); }");
        yield return Case("foreach (var chunk in System.Linq.Enumerable.Chunk(new[] { 1 }, 100)) { foreach (var item in chunk) { context.SaveChanges(); } }");
        yield return Case("var processed = 0; foreach (var i in new[] { 1 }) { processed++; if (i > 0) { context.SaveChanges(); } }");
        yield return new object[]
        {
            AnalyzerTestHarness.ContextSource(
                "static void Run(TestContext context, List<Item> items) { foreach (var item in items) { if (item.IsTransient) { context.SaveChanges(); } } }",
                "sealed class Item { public bool IsTransient { get; set; } }"),
            1,
        };
        yield return new object[]
        {
            AnalyzerTestHarness.ContextSource(
                "static void Run(TestContext context, List<Order> orders) { foreach (var order in orders) { var lines = order.Lines; foreach (var line in lines) { } context.SaveChanges(); } }",
                "sealed class Order { public List<int> Lines { get; set; } = new(); }"),
            1,
        };
        yield return new object[]
        {
            AnalyzerTestHarness.ContextSource(
                "static void Run(OverriddenContext context) { foreach (var i in new[] { 1 }) { context.SaveChanges(); } }",
                "sealed class OverriddenContext : DbContext { public override int SaveChanges() => base.SaveChanges(); }"),
            1,
        };
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return new object[] { "sealed class Store { public void SaveChanges() { } } static class S { static void M(Store s) { foreach (var i in new[]{1}) s.SaveChanges(); } }" };
        yield return new object[] { AnalyzerTestHarness.ContextSource("static void Run(TestContext context) { context.SaveChanges(); }") };
        yield return new object[] { AnalyzerTestHarness.ContextSource("static void Run(TestContext context) { foreach (var i in new[]{1}) { Action action = () => context.SaveChanges(); _ = action; } }") };
        yield return new object[] { AnalyzerTestHarness.ContextSource("static void Run(TestContext context) { foreach (var i in new[]{1}) { Action action = delegate { context.SaveChanges(); }; _ = action; } }") };
        yield return new object[] { AnalyzerTestHarness.ContextSource("static void Run(TestContext context) { foreach (var i in new[]{1}) { void Local() { context.SaveChanges(); } _ = (Action)Local; } }") };
        yield return new object[] { AnalyzerTestHarness.ContextSource("static void Run(TestContext context) { foreach (var i in new[]{1}) { /* context.SaveChanges(); */ } }") };
        yield return new object[] { AnalyzerTestHarness.ContextSource("static void Run(TestContext context) { foreach (var i in new[]{1}) { var text = \"context.SaveChanges();\"; _ = text; } }") };
        yield return new object[] { AnalyzerTestHarness.ContextSource("static void Run(TestContext context) { foreach (var i in new[]{1}) { #if NEVER context.SaveChanges(); #endif } }") };
        yield return BatchCase("foreach (var chunk in System.Linq.Enumerable.Chunk(new[] { 1, 2, 3 }, 2)) { context.SaveChanges(); }");
        yield return new object[] { AnalyzerTestHarness.ContextSource("static async Task Run(TestContext context) { foreach (var chunk in System.Linq.Enumerable.Chunk(new[] { 1 }, 100)) { foreach (var item in chunk) { } await context.SaveChangesAsync(); } }") };
        yield return BatchCase("while (Next(out var page)) { foreach (var item in page) { } context.SaveChanges(); }");
        yield return BatchCase("while (Load() is { Count: > 0 } page) { context.SaveChanges(); }");
        yield return BatchCase("List<int> page; while ((page = Load()).Count > 0) { context.SaveChanges(); }");
        yield return BatchCase("var index = 0; while (true) { var page = Load(++index); foreach (var item in page) { } context.SaveChanges(); if (page.Count == 0) { break; } }");
        yield return BatchCase("var index = 0; while (true) { var page = Load(++index); foreach (var item in page.ToArray()) { } context.SaveChanges(); if (page.Count == 0) { break; } }");
        yield return BatchCase("var processed = 0; foreach (var i in new[] { 1 }) { processed++; if (processed >= 100) { context.SaveChanges(); processed = 0; } }");
        yield return BatchCase("for (var i = 0; i < 10; i++) { if (i % 100 == 0) { context.SaveChanges(); } }");
        yield return BatchCase("var buffer = new List<int>(); foreach (var i in new[] { 1 }) { buffer.Add(i); if (buffer.Count >= 100) { context.SaveChanges(); buffer.Clear(); } }");
        yield return new object[] { "static class S { static void M(dynamic context) { foreach (var i in new[]{1}) context.SaveChanges(); } }" };
        yield return new object[] { AnalyzerTestHarness.ContextSource("static void Run(TestContext context) { foreach (var i in new[]{1}) { Helper(context); } } static void Helper(TestContext context) { context.SaveChanges(); }") };
        yield return new object[] { AnalyzerTestHarness.ContextSource("static void Run(TestContext context) { foreach (var i in new[]{1}) { ((Action)(() => context.SaveChanges()))(); } }") };
        yield return new object[]
        {
            AnalyzerTestHarness.ContextSource(
                "static void Run(HidingContext context) { foreach (var i in new[]{1}) { context.SaveChanges(); } }",
                "sealed class HidingContext : DbContext { public new int SaveChanges() => 0; }"),
        };
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    [Trait("Spec", "efd001-detection/Synchronous EF Core save")]
    [Trait("Spec", "efd001-detection/Asynchronous EF Core save")]
    [Trait("Spec", "efd001-detection/Custom DbContext subclass")]
    [Trait("Spec", "efd001-detection/Overridden save method")]
    [Trait("Spec", "efd001-detection/Each supported loop form")]
    [Trait("Spec", "efd001-detection/Nested ordinary control-flow block")]
    [Trait("Spec", "efd001-detection/Per-item loop nested in a batch loop")]
    [Trait("Spec", "efd001-detection/Ordinary condition is not a batch threshold")]
    public async Task ReportsEachPositiveFixture(string source, int expectedCount)
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Count(static diagnostic => diagnostic.Id == SaveChangesInLoopAnalyzer.DiagnosticId));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    [Trait("Spec", "efd001-detection/Unrelated same-name method")]
    [Trait("Spec", "efd001-detection/Unresolved invocation")]
    [Trait("Spec", "efd001-detection/Save outside a loop")]
    [Trait("Spec", "efd001-detection/Method declaration near loop syntax")]
    [Trait("Spec", "efd001-detection/Lambda declared inside a loop")]
    [Trait("Spec", "efd001-detection/Local function declared inside a loop")]
    [Trait("Spec", "efd001-detection/Comments strings and inactive code")]
    [Trait("Spec", "efd001-detection/Save per chunk")]
    [Trait("Spec", "efd001-detection/Save per page declared by the loop condition")]
    [Trait("Spec", "efd001-detection/Save per page loaded in the loop body")]
    [Trait("Spec", "efd001-detection/Threshold flush with a counter")]
    [Trait("Spec", "efd001-detection/Threshold flush with a modulo or a buffer count")]
    public async Task DoesNotReportNegativeFixture(string source)
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id == SaveChangesInLoopAnalyzer.DiagnosticId);
    }

    [Fact]
    [Trait("Spec", "efd001-detection/Multiple matching calls")]
    [Trait("Spec", "efd001-detection/Source span accuracy")]
    public async Task ReportsEveryInvocationWithExactLocationsAndProperties()
    {
        var source = AnalyzerTestHarness.ContextSource("""
            static void Run(TestContext context)
            {
                foreach (var i in new[] { 1 })
                {
                    context.SaveChanges();
                    context.SaveChanges();
                }
            }
            """);

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source);
        var matches = diagnostics.Where(static diagnostic => diagnostic.Id == SaveChangesInLoopAnalyzer.DiagnosticId).ToArray();

        Assert.Equal(2, matches.Length);
        Assert.All(matches, static diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("high", diagnostic.Properties[SaveChangesInLoopAnalyzer.ConfidenceProperty]);
            Assert.Contains("database round trips", diagnostic.Properties[SaveChangesInLoopAnalyzer.ImpactProperty]);
            Assert.Contains("intentional per-item transactions", diagnostic.Properties[SaveChangesInLoopAnalyzer.RemediationProperty]);
            Assert.Equal("EFD001", diagnostic.Properties[SaveChangesInLoopAnalyzer.DocumentationProperty]);
        });
        Assert.Equal(matches[0].Location.GetLineSpan().StartLinePosition.Line + 2, matches[1].Location.GetLineSpan().StartLinePosition.Line + 1);
        Assert.Equal("context.SaveChanges()", matches[0].Location.SourceTree!.GetText().ToString(matches[0].Location.SourceSpan));
    }

    [Fact]
    public async Task DoesNothingWhenEntityFrameworkIsUnavailable()
    {
        const string source = "static class S { static void M(dynamic c) { foreach (var i in new[]{1}) c.SaveChanges(); } }";

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, includeEntityFramework: false);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd001-detection/Pragma suppression")]
    public async Task PragmaSuppressionIsHonored()
    {
        var source = AnalyzerTestHarness.ContextSource("""
            static void Run(TestContext context)
            {
            #pragma warning disable EFD001 // Intentional per-item transaction.
                foreach (var i in new[] { 1 }) { context.SaveChanges(); }
            #pragma warning restore EFD001
            }
            """);

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd001-detection/Editor configuration suppression")]
    public async Task EditorConfigurationSeverityNoneIsHonored()
    {
        var source = AnalyzerTestHarness.ContextSource("static void Run(TestContext context) { foreach (var i in new[]{1}) context.SaveChanges(); }");

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, configuredSeverity: ReportDiagnostic.Suppress));
    }

    [Fact]
    [Trait("Spec", "efd001-detection/SuppressMessage with justification")]
    public async Task SuppressMessageWithJustificationIsHonored()
    {
        var source = AnalyzerTestHarness.ContextSource("""
            [SuppressMessage("Performance", "EFD001", Justification = "Intentional independent transactions.")]
            static void Run(TestContext context)
            {
                foreach (var i in new[] { 1 }) context.SaveChanges();
            }
            """);

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd001-detection/Loop inside a deferred callable")]
    public async Task LoopInsideDeferredCallableIsReported()
    {
        var source = AnalyzerTestHarness.ContextSource("""
            static void Run(TestContext context)
            {
                Action action = () => { foreach (var i in new[] { 1 }) context.SaveChanges(); };
                action();
            }
            """);

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source);

        Assert.Single(diagnostics, static diagnostic => diagnostic.Id == SaveChangesInLoopAnalyzer.DiagnosticId);
    }

    [Fact]
    [Trait("Spec", "efd001-detection/Remediation is presented")]
    public void DescriptorIsCompleteAndAvoidsAbsoluteClaims()
    {
        Assert.Equal("EFD001", SaveChangesInLoopAnalyzer.Rule.Id);
        Assert.Equal(DiagnosticSeverity.Warning, SaveChangesInLoopAnalyzer.Rule.DefaultSeverity);
        Assert.Contains("may cause", SaveChangesInLoopAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("always", SaveChangesInLoopAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("high", SaveChangesInLoopAnalyzer.Confidence);
        Assert.Contains("saving once after the loop", SaveChangesInLoopAnalyzer.Remediation, StringComparison.Ordinal);
        Assert.Contains("per-item transactions", SaveChangesInLoopAnalyzer.Remediation, StringComparison.Ordinal);
        Assert.Contains("generated-key", SaveChangesInLoopAnalyzer.Remediation, StringComparison.Ordinal);
    }

    private static object[] Case(string statement) =>
    [
        AnalyzerTestHarness.ContextSource($"static void Run(TestContext context) {{ {statement} }}"),
        1,
    ];

    private const string PageHelpers = "static List<int> Load() => new(); static List<int> Load(int index) => new(); static bool Next(out List<int> page) { page = new(); return false; }";

    private static object[] BatchCase(string statement) =>
    [
        AnalyzerTestHarness.ContextSource($"static void Run(TestContext context) {{ {statement} }} " + PageHelpers),
    ];

    private static object[] AsyncCase(string statement) =>
    [
        AnalyzerTestHarness.ContextSource($"static async Task Run(TestContext context, CancellationToken token) {{ {statement} }}"),
        1,
    ];
}
