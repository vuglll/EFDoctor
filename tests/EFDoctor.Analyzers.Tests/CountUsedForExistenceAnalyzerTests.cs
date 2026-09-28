using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class CountUsedForExistenceAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("return context.Entities.Count() > 0;");
        yield return Case("return context.Entities.Count() != 0;");
        yield return Case("return context.Entities.Count() >= 1;");
        yield return Case("return context.Entities.Count() == 0;");
        yield return Case("return context.Entities.Count() <= 0;");
        yield return Case("return context.Entities.Count() < 1;");
        yield return Case("return 0 < context.Entities.Count();");
        yield return Case("return 0 == context.Entities.Count();");
        yield return Case("return context.Entities.Where(entity => entity.Id > 0).Select(entity => entity.Id).Count() > 0;");
        yield return Case("return context.Set<Entity>().Count() > 0;");
        yield return Case("return Queryable.Count(context.Entities) > 0;");
        yield return Case("return context.Entities.Count(entity => entity.Id > 0) > 0;");
        yield return Case("const int None = 0; return context.Entities.Count() != None;");
        yield return Case("return context.Entities.Count() > 0L;");
        yield return AsyncCase("return (await context.Entities.CountAsync()) > 0;");
        yield return AsyncCase("return (await context.Entities.CountAsync(token)) == 0;");
        yield return AsyncCase("return (await context.Entities.CountAsync(entity => entity.Id > 0, token)) >= 1;");
        yield return AsyncCase("return 1 > (await context.Entities.CountAsync());");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return new object[] { QuerySource("static bool Run(TestContext context) => new[] { 1, 2 }.Count() > 0;") };
        yield return new object[] { QuerySource("static bool Run(IQueryable<Entity> query) => query.Count() > 0;") };
        yield return new object[] { CaseSource("return context.Entities.Count() == 1;") };
        yield return new object[] { CaseSource("return context.Entities.Count() > 1;") };
        yield return new object[] { QuerySource("static int Run(TestContext context) => context.Entities.Count();") };
        yield return new object[] { QuerySource("static bool Run(TestContext context) { var count = context.Entities.Count(); return count > 0; }") };
        yield return new object[] { CaseSource("return context.Entities.Count() + 1 > 0;") };
        yield return new object[] { CaseSource("return context.Entities.LongCount() > 0;") };
        yield return new object[] { CaseSource("return context.Entities.Count() is > 0;") };
        yield return new object[] { "static class Subject { static bool Run(dynamic query) => query.Count() > 0; }" };
        yield return new object[] { QuerySource("static bool Run(TestContext context) { /* context.Entities.Count() > 0 */ return false; }") };
        yield return new object[] { QuerySource("static bool Run(TestContext context) { var text = \"context.Entities.Count() > 0\"; return text.Length > 0; }") };
        yield return new object[] { QuerySource("static bool Run(TestContext context) { #if NEVER return context.Entities.Count() > 0; #else return false; #endif }") };
        yield return new object[] { QuerySource("static bool Run(IQueryable<Entity> query, TestContext context) => query.Count(entity => context.Entities.Any()) > 0;") };
        yield return new object[] { QuerySource("static bool Run(Store store) => store.Count() > 0;", "sealed class Store { public int Count() => 1; }") };
        yield return new object[] { QuerySource("static async Task<bool> Run(Store store) => (await store.CountAsync()) > 0;", "sealed class Store { public Task<int> CountAsync() => Task.FromResult(1); }") };
        yield return new object[] { QuerySource("static bool Run(TestContext context) => HasRows(context.Entities.Count()); static bool HasRows(int count) => count > 0;") };
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsEachPositiveFixture(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        var actualCount = diagnostics.Count(static diagnostic => diagnostic.Id == CountUsedForExistenceAnalyzer.DiagnosticId);
        Assert.True(actualCount == expectedCount, $"Expected {expectedCount} EFD002 diagnostics but found {actualCount}. Source:\n{source}");
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportNegativeFixture(string source)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id == CountUsedForExistenceAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task ReportsEveryInvocationWithExactLocationsAndProperties()
    {
        var source = QuerySource("""
            static bool Run(TestContext context)
            {
                var first = context.Entities.Count() > 0;
                var second = context.Entities.Count(entity => entity.Id > 0) == 0;
                return first || second;
            }
            """);

        var matches = (await AnalyzeAsync(source))
            .Where(static diagnostic => diagnostic.Id == CountUsedForExistenceAnalyzer.DiagnosticId)
            .ToArray();

        Assert.Equal(2, matches.Length);
        Assert.All(matches, static diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
            Assert.Contains("process more matching rows", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
            Assert.Contains("Any", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]);
            Assert.Equal("EFD002", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        });
        Assert.Equal("context.Entities.Count()", matches[0].Location.SourceTree!.GetText().ToString(matches[0].Location.SourceSpan));
        Assert.Equal("context.Entities.Count(entity => entity.Id > 0)", matches[1].Location.SourceTree!.GetText().ToString(matches[1].Location.SourceSpan));
        Assert.Contains("count > 0", matches[0].Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Contains("the negation of Any", matches[1].Properties[DiagnosticPropertyNames.SuggestedRemediation]);
        Assert.Equal(matches[0].Location.GetLineSpan().StartLinePosition.Line + 1, matches[1].Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public async Task StaticEfCountAsyncExtensionIsRecognized()
    {
        var source = QuerySource("static async Task<bool> Run(TestContext context) => (await EntityFrameworkQueryableExtensions.CountAsync(context.Entities)) > 0;");

        Assert.Single(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == CountUsedForExistenceAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task PragmaSuppressionPreservesUnsuppressedNeighbor()
    {
        var source = QuerySource("""
            static bool Run(TestContext context)
            {
            #pragma warning disable EFD002 // Exact form retained for provider-specific diagnostics.
                var suppressed = context.Entities.Count() > 0;
            #pragma warning restore EFD002
                var reported = context.Entities.Count() > 0;
                return suppressed || reported;
            }
            """);

        Assert.Single(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == CountUsedForExistenceAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task EditorConfigurationSeverityNoneIsHonored()
    {
        var source = QuerySource("static bool Run(TestContext context) => context.Entities.Count() > 0;");

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new CountUsedForExistenceAnalyzer(),
            diagnosticId: CountUsedForExistenceAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SuppressMessageWithJustificationIsHonored()
    {
        var source = QuerySource("""
            [SuppressMessage("Performance", "EFD002", Justification = "Count is retained for provider diagnostics.")]
            static bool Run(TestContext context) => context.Entities.Count() > 0;
            """);

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public void DescriptorIsCompleteAndAvoidsAbsoluteClaims()
    {
        Assert.Equal("EFD002", CountUsedForExistenceAnalyzer.Rule.Id);
        Assert.Equal(DiagnosticSeverity.Warning, CountUsedForExistenceAnalyzer.Rule.DefaultSeverity);
        Assert.Contains("may process", CountUsedForExistenceAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("always", CountUsedForExistenceAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("high", CountUsedForExistenceAnalyzer.Confidence);
        Assert.Equal("EFD002", CountUsedForExistenceAnalyzer.DocumentationKey);
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        return AnalyzerTestHarness.AnalyzeAsync(
            source,
            analyzer: new CountUsedForExistenceAnalyzer(),
            diagnosticId: CountUsedForExistenceAnalyzer.DiagnosticId);
    }

    private static object[] Case(string body) => [CaseSource(body), 1];

    private static object[] AsyncCase(string body) =>
    [
        QuerySource($"static async Task<bool> Run(TestContext context, CancellationToken token) {{ {body} }}"),
        1,
    ];

    private static string CaseSource(string body) => QuerySource($"static bool Run(TestContext context) {{ {body} }}");

    private static string QuerySource(string member, string additionalTypes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Entity
        {
            public int Id { get; set; }
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Entity> Entities => Set<Entity>();
        }

        {{additionalTypes}}

        static class Subject
        {
            {{member}}
        }
        """;
}
