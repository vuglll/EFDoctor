using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using EFDoctor.Analyzers;

namespace EFDoctor.Analyzers.Tests;

public sealed class StaticDbContextFieldAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("sealed class Holder { private static AppDbContext Context = null!; }", 1);
        yield return Case("sealed class Holder { internal static DbContext Context = null!; }", 1);
        yield return Case("sealed class Holder { public static AppDbContext A = null!; private static AppDbContext B = null!; }", 2);
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Case("sealed class Holder { private AppDbContext Context = null!; }");
        yield return Case("sealed class Holder { private static string Name = \"\"; }");
        yield return Case("namespace Other { sealed class DbContext { } } sealed class Holder { private static Other.DbContext Context = null!; }");
        yield return Case("sealed class Holder { private AppDbContext Context = null!; public Holder(AppDbContext context) => Context = context; }");
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsPositiveFixture(string source, int expectedCount)
    {
        var matches = await AnalyzeAsync(source);
        Assert.Equal(expectedCount, matches.Count(static diagnostic => diagnostic.Id == StaticDbContextFieldAnalyzer.DiagnosticId));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportNegativeFixture(string source)
    {
        var matches = await AnalyzeAsync(source);
        Assert.DoesNotContain(matches, static diagnostic => diagnostic.Id == StaticDbContextFieldAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task FindingCarriesEvidenceConfidenceAndDocumentationKey()
    {
        var source = Source("sealed class Holder { private static AppDbContext Context = null!; }");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains("declared static", diagnostic.Properties[DiagnosticPropertyNames.Evidence]!, StringComparison.Ordinal);
        Assert.Equal("EFD021", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
    }

    [Fact]
    public async Task PragmaSuppressionIsHonored()
    {
        var source = Source("""
            sealed class Holder
            {
            #pragma warning disable EFD021
                private static AppDbContext Context = null!;
            #pragma warning restore EFD021
            }
            """);

        Assert.DoesNotContain(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == StaticDbContextFieldAnalyzer.DiagnosticId);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source.Contains("class AppDbContext", StringComparison.Ordinal) ? source : Source(source),
            analyzer: new StaticDbContextFieldAnalyzer(),
            diagnosticId: StaticDbContextFieldAnalyzer.DiagnosticId);

    private static object[] Case(string member, int expectedCount) => [Source(member), expectedCount];

    private static object[] Case(string member) => [Source(member)];

    private static string Source(string member) => $$"""
        using System.Diagnostics.CodeAnalysis;
        using Microsoft.EntityFrameworkCore;

        sealed class AppDbContext : DbContext
        {
        }

        {{member}}
        """;
}
