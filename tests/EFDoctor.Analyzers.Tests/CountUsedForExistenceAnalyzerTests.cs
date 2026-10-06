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

    public static IEnumerable<object[]> StoredCountCases()
    {
        foreach (var comparison in new[] { "count > 0", "count != 0", "count >= 1", "0 < count", "(count) > 0" })
        {
            yield return new object[] { $"static bool Run(TestContext context) {{ var count = context.Entities.Count(); return {comparison}; }}", "Use Any for this existence test", comparison };
        }

        foreach (var comparison in new[] { "count == 0", "count <= 0", "count < 1", "0 == count" })
        {
            yield return new object[] { $"static bool Run(TestContext context) {{ var count = context.Entities.Count(); return {comparison}; }}", "Use the negation of Any for this existence test", comparison };
        }

        yield return new object[] { "static bool Run(TestContext context) { var count = context.Entities.Count(entity => entity.Id == 42); return count >= 1; }", "Use Any for this existence test", "count >= 1" };
        yield return new object[] { "static bool Run(TestContext context) { int count = context.Entities.Where(entity => entity.Id > 0).Count(); if (count == 0) { return false; } return true; }", "Use the negation of Any for this existence test", "count == 0" };
        yield return new object[] { "static bool Run(TestContext context) { long count = context.Entities.Count(); return count > 0; }", "Use Any for this existence test", "count > 0" };
        yield return new object[] { "static async Task<bool> Run(TestContext context) { var count = await context.Entities.CountAsync(); return count > 0; }", "Use AnyAsync for this existence test", "count > 0" };
        yield return new object[] { "static async Task<bool> Run(TestContext context, CancellationToken token) { var count = await context.Entities.CountAsync(entity => entity.Id > 0, token); return count < 1; }", "Use the negation of AnyAsync for this existence test", "count < 1" };
    }

    [Theory]
    [MemberData(nameof(StoredCountCases))]
    public async Task ReportsCountStoredInALocalAndOnlyComparedForExistence(string member, string expectedRemediation, string comparison)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource(member)));

        Assert.Contains("Count", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan), StringComparison.Ordinal);
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("This EF Core count is used only to test existence and may process more rows than an existence query", diagnostic.GetMessage());
        Assert.StartsWith(expectedRemediation, diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains($"stored in local 'count', which this method only compares for existence: '{comparison}'.", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoredCountComparedBothWaysNamesEveryComparison()
    {
        var source = QuerySource("static string Run(TestContext context) { var count = context.Entities.Count(); if (count == 0) { return \"none\"; } return count > 0 ? \"some\" : \"none\"; }");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Contains("'count == 0', 'count > 0'", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.StartsWith("Use Any, negated where the code tests for no rows, for this existence test", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.Count(); return count; }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.Count(); Console.WriteLine(count); return count > 0; }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.Count(); return count > 0 ? count : -1; }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.Count(); return count == 5; }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.Count(); return count > 1; }")]
    [InlineData("static object Run(TestContext context, int limit) { var count = context.Entities.Count(); return count > limit; }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.Count(); return count + 1 > 0; }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.Count(); return $\"{count}\"; }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.Count(); Func<bool> any = () => count > 0; return any(); }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.Count(); bool Any() => count > 0; return Any(); }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.Count(); count = 0; return count > 0; }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.Count(); count++; return count > 0; }")]
    [InlineData("static void Run(TestContext context) { var count = context.Entities.Count(); }")]
    [InlineData("static object Run(TestContext context) { int count; count = context.Entities.Count(); return count > 0; }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.LongCount(); return count > 0; }")]
    [InlineData("static object Run(IQueryable<Entity> query) { var count = query.Count(); return count > 0; }")]
    [InlineData("static object Run(List<Entity> list) { var count = list.Count(); return count > 0; }")]
    [InlineData("static object Run(TestContext context) { var count = context.Entities.CountAsync(); return count.Result > 0; }")]
    public async Task DoesNotReportStoredCountUsedAsAValue(string member)
    {
        Assert.Empty(await AnalyzeAsync(QuerySource(member)));
    }

    public static IEnumerable<object[]> EntityLoadCases()
    {
        yield return new object[] { "static bool Run(TestContext context) => context.Entities.FirstOrDefault() != null;", "Use Any with the same predicate", "'context.Entities.FirstOrDefault() != null'" };
        yield return new object[] { "static bool Run(TestContext context) => context.Entities.FirstOrDefault() == null;", "Use Any with the same predicate", "'context.Entities.FirstOrDefault() == null'" };
        yield return new object[] { "static bool Run(TestContext context) => null != context.Entities.FirstOrDefault();", "Use Any with the same predicate", "'null != context.Entities.FirstOrDefault()'" };
        yield return new object[] { "static bool Run(TestContext context) => context.Entities.Where(entity => entity.Id > 0).OrderBy(entity => entity.Id).AsNoTracking().FirstOrDefault(entity => entity.Id == 42) is not null;", "Use Any with the same predicate", "is not null'" };
        yield return new object[] { "static bool Run(TestContext context) => (context.Set<Entity>().FirstOrDefault()) is null;", "Use Any with the same predicate", "is null'" };
        yield return new object[] { "static async Task<bool> Run(TestContext context) => await context.Entities.FirstOrDefaultAsync() != null;", "Use AnyAsync with the same predicate", "!= null'" };
        yield return new object[] { "static async Task<bool> Run(TestContext context, CancellationToken token) => (await context.Entities.FirstOrDefaultAsync(entity => entity.Id == 42, token)) is null;", "Use AnyAsync with the same predicate", "is null'" };

        yield return new object[] { "static bool Run(TestContext context) { var entity = context.Entities.FirstOrDefault(); return entity is not null; }", "Use Any with the same predicate", "stored in local 'entity', which this method only checks for null: 'entity is not null'" };
        yield return new object[] { "static bool Run(TestContext context) { var entity = context.Entities.FirstOrDefault(e => e.Id == 42); return entity is null; }", "Use Any with the same predicate", "'entity is null'" };
        yield return new object[] { "static bool Run(TestContext context) { Entity? entity = context.Entities.FirstOrDefault(); if (entity == null) { return false; } return entity != null; }", "Use Any with the same predicate", "'entity == null', 'entity != null'" };
        yield return new object[] { "static async Task<bool> Run(TestContext context) { var entity = await context.Entities.FirstOrDefaultAsync(e => e.Id == 42); return entity != null; }", "Use AnyAsync with the same predicate", "'entity != null'" };
        yield return new object[] { "static bool Run(TestContext context) { var entity = Queryable.FirstOrDefault(context.Entities); return (entity) is not null; }", "Use Any with the same predicate", "'(entity) is not null'" };
    }

    [Theory]
    [MemberData(nameof(EntityLoadCases))]
    public async Task ReportsFirstOrDefaultUsedOnlyAsANullCheck(string member, string expectedRemediation, string expectedEvidence)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource(member)));

        Assert.Contains("FirstOrDefault", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan), StringComparison.Ordinal);
        Assert.Equal("medium", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.StartsWith("This EF Core FirstOrDefault", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.EndsWith("result is used only to test existence and loads an entity where an existence query would do", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal(CountUsedForExistenceAnalyzer.EntityLoadImpact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("at most one row", CountUsedForExistenceAnalyzer.EntityLoadImpact, StringComparison.Ordinal);
        var remediation = diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]!;
        Assert.StartsWith(expectedRemediation, remediation, StringComparison.Ordinal);
        Assert.Contains("negated where the code tests for null", remediation, StringComparison.Ordinal);
        Assert.Contains("Keep the load when the entity must be tracked", remediation, StringComparison.Ordinal);
        Assert.Contains("DbSet origin", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains(expectedEvidence, diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Theory]
    // The entity is used.
    [InlineData("static object? Run(TestContext context) { var entity = context.Entities.FirstOrDefault(); return entity; }")]
    [InlineData("static object? Run(TestContext context) { var entity = context.Entities.FirstOrDefault(); return entity is null ? null : entity.Id; }")]
    [InlineData("static object? Run(TestContext context) { var entity = context.Entities.FirstOrDefault(); if (entity is null) { return 0; } Console.WriteLine(entity); return 1; }")]
    [InlineData("static object? Run(TestContext context) { var entity = context.Entities.FirstOrDefault(); return entity?.Id; }")]
    [InlineData("static object? Run(TestContext context) => context.Entities.FirstOrDefault()?.Id;")]
    [InlineData("static object? Run(TestContext context) => context.Entities.FirstOrDefault() ?? new Entity();")]
    [InlineData("static object? Run(TestContext context, Entity other) => context.Entities.FirstOrDefault() == other;")]
    [InlineData("static object? Run(TestContext context) { var entity = context.Entities.FirstOrDefault(); Func<bool> exists = () => entity != null; return exists(); }")]
    [InlineData("static object? Run(TestContext context) { var entity = context.Entities.FirstOrDefault(); entity = null; return entity != null; }")]
    [InlineData("static object? Run(TestContext context) { var entity = context.Entities.FirstOrDefault(); return entity is { Id: 1 }; }")]
    [InlineData("static object? Run(TestContext context) { var entity = context.Entities.FirstOrDefault(); return entity is Entity; }")]
    [InlineData("static void Run(TestContext context) { var entity = context.Entities.FirstOrDefault(); }")]
    // The result can be null while a row exists.
    [InlineData("static object? Run(TestContext context) => context.Entities.Select(entity => entity.Note).FirstOrDefault() != null;")]
    [InlineData("static object? Run(TestContext context) => context.Entities.Select(entity => entity.Parent).FirstOrDefault() is null;")]
    [InlineData("static object? Run(TestContext context) { var note = context.Entities.Select(entity => entity.Note).FirstOrDefault(); return note is not null; }")]
    // Other element operators, and unproven sources.
    [InlineData("static object? Run(TestContext context) => context.Entities.SingleOrDefault() != null;")]
    [InlineData("static object? Run(TestContext context) => context.Entities.OrderBy(entity => entity.Id).LastOrDefault() != null;")]
    [InlineData("static object? Run(TestContext context) => context.Entities.Find(1) != null;")]
    [InlineData("static object? Run(TestContext context) => context.Entities.First() != null;")]
    [InlineData("static object? Run(TestContext context, Entity fallback) => context.Entities.AsEnumerable().FirstOrDefault(fallback) != null;")]
    [InlineData("static object? Run(TestContext context) => context.Entities.AsEnumerable().FirstOrDefault() != null;")]
    [InlineData("static object? Run(TestContext context) => context.Entities.ToList().FirstOrDefault() != null;")]
    [InlineData("static object? Run(IQueryable<Entity> query) => query.FirstOrDefault() != null;")]
    [InlineData("static object? Run(List<Entity> list) => list.FirstOrDefault() != null;")]
    [InlineData("static object? Run(TestContext context) => context.Entities.FirstOrDefaultAsync().Result != null;")]
    public async Task DoesNotReportFirstOrDefaultWhoseEntityIsUsedOrMayBeNull(string member)
    {
        Assert.Empty(await AnalyzeAsync(QuerySource(member)));
    }

    [Fact]
    public async Task RecordEntityNullChecksAreRecognized()
    {
        var source = QuerySource(
            "static bool Run(RecordContext context) { var row = context.Rows.FirstOrDefault(); return row == null || context.Rows.FirstOrDefault(r => r.Id == 1) != null; }",
            "sealed record Row(int Id); sealed class RecordContext : DbContext { public DbSet<Row> Rows => Set<Row>(); }");

        Assert.Equal(2, (await AnalyzeAsync(source)).Length);
    }

    [Fact]
    public async Task NullCheckedFirstOrDefaultHonorsSuppression()
    {
        var source = QuerySource("""
            #pragma warning disable EFD002 // Reviewed: the entity is loaded so that it is tracked.
            static bool Suppressed(TestContext context) => context.Entities.FirstOrDefault() != null;
            #pragma warning restore EFD002
            static bool Reported(TestContext context) => context.Entities.FirstOrDefault() != null;
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Contains("Reported", diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DescriptorIsCompleteAndAvoidsAbsoluteClaims()
    {
        Assert.Equal("EFD002", CountUsedForExistenceAnalyzer.Rule.Id);
        Assert.Equal(DiagnosticSeverity.Warning, CountUsedForExistenceAnalyzer.Rule.DefaultSeverity);
        Assert.Contains("is used only to test existence", CountUsedForExistenceAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
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
            public string? Note { get; set; }
            public Entity? Parent { get; set; }
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
