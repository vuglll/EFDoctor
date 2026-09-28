using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class UnsafeRawSqlAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(TestContext context, int id) => context.Entities.FromSqlRaw($\"SELECT * FROM Entities WHERE Id = {id}\");");
        yield return Case("static int Run(TestContext context, string name) => context.Database.ExecuteSqlRaw(\"DELETE FROM Entities WHERE Name = '\" + name + \"'\");");
        yield return Case("static Task<int> Run(TestContext context, string name) => context.Database.ExecuteSqlRawAsync($\"DELETE FROM Entities WHERE Name = '{name}'\");");
        yield return Case("static Task<int> Run(TestContext context, int id) => context.Database.ExecuteSqlRawAsync(\"DELETE FROM Entities WHERE Id = \" + id, CancellationToken.None);");
        yield return Case("static object Run(TestContext context, int id) => RelationalQueryableExtensions.FromSqlRaw(context.Entities, $\"SELECT * FROM Entities WHERE Id = {id}\");");
        yield return Case("static int Run(TestContext context, int id) => RelationalDatabaseFacadeExtensions.ExecuteSqlRaw(context.Database, $\"DELETE FROM Entities WHERE Id = {id}\");");
        yield return Case("static object Run(TestContext context, int id) { var sql = $\"SELECT * FROM Entities WHERE Id = {id}\"; return context.Entities.FromSqlRaw(sql); }");
        yield return Case("static int Run(TestContext context, string table) { string sql; sql = \"DELETE FROM \" + table; return context.Database.ExecuteSqlRaw(sql); }");
        yield return Case("static object Run(TestContext context, int id) { var fragment = id.ToString(); var sql = \"SELECT * FROM Entities WHERE Id = \" + fragment; return context.Entities.FromSqlRaw(sql); }");
        yield return Case("static int Run(TestContext context, string suffix) => context.Database.ExecuteSqlRaw((\"DELETE FROM Entities \" + suffix));");
        yield return Case("static int Run(TestContext context, int id) { context.Database.ExecuteSqlRaw($\"DELETE FROM Entities WHERE Id = {id}\"); return context.Database.ExecuteSqlRaw(\"DELETE FROM Entities WHERE Id = \" + id); }", expectedCount: 2);
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static object Run(TestContext context) => context.Entities.FromSqlRaw(\"SELECT * FROM Entities\");");
        yield return Negative("static object Run(TestContext context, int id) => context.Entities.FromSqlRaw(\"SELECT * FROM Entities WHERE Id = {0}\", id);");
        yield return Negative("static int Run(TestContext context, int id) => context.Database.ExecuteSqlRaw(\"DELETE FROM Entities WHERE Id = {0}\", id);");
        yield return Negative("static Task<int> Run(TestContext context, int id) => context.Database.ExecuteSqlRawAsync(\"DELETE FROM Entities WHERE Id = {0}\", new object[] { id });");
        yield return Negative("static object Run(TestContext context, int id) => context.Entities.FromSqlInterpolated($\"SELECT * FROM Entities WHERE Id = {id}\");");
        yield return Negative("static int Run(TestContext context, int id) => context.Database.ExecuteSqlInterpolated($\"DELETE FROM Entities WHERE Id = {id}\");");
        yield return Negative("static Task<int> Run(TestContext context, int id) => context.Database.ExecuteSqlInterpolatedAsync($\"DELETE FROM Entities WHERE Id = {id}\");");
        yield return Negative("static object Run(TestContext context) => context.Entities.FromSqlRaw(\"SELECT * \" + \"FROM Entities\");");
        yield return Negative("static int Run(FakeDatabase database, int id) => database.ExecuteSqlRaw($\"DELETE {id}\");", "sealed class FakeDatabase { public int ExecuteSqlRaw(string sql) => 0; }");
        yield return Negative("static object Run(FakeSet set, int id) => set.FromSqlRaw($\"SELECT {id}\");", "sealed class FakeSet { public object FromSqlRaw(string sql) => new(); }");
        yield return Negative("static object Run(TestContext context, string sql) => context.Entities.FromSqlRaw(sql);");
        yield return Negative("static object Run(TestContext context, int id, bool useDynamic) { var sql = \"SELECT * FROM Entities\"; if (useDynamic) sql = $\"SELECT * FROM Entities WHERE Id = {id}\"; return context.Entities.FromSqlRaw(sql); }");
        yield return Negative("static object Run(TestContext context, int id) { string sql; if (id > 0) sql = $\"SELECT * FROM Entities WHERE Id = {id}\"; else sql = \"SELECT * FROM Entities\"; return context.Entities.FromSqlRaw(sql); }");
        yield return Negative("static object Run(TestContext context, int id) { var sql = $\"SELECT {id}\"; Rewrite(ref sql); return context.Entities.FromSqlRaw(sql); } static void Rewrite(ref string sql) => sql = \"SELECT 1\";");
        yield return Negative("static string Run(int id) => $\"SELECT {id}\";");
        yield return new object[] { GeneratedSource("static object Run(TestContext context, int id) => context.Entities.FromSqlRaw($\"SELECT {id}\");"), 0 };
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsPositiveCases(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Count(static diagnostic => diagnostic.Id == UnsafeRawSqlAnalyzer.DiagnosticId));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportNegativeCases(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Count(static diagnostic => diagnostic.Id == UnsafeRawSqlAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task ReportsSqlArgumentWithActionableProperties()
    {
        const string expression = "$\"SELECT * FROM Entities WHERE Id = {id}\"";
        var source = CaseSource($"static object Run(TestContext context, int id) => context.Entities.FromSqlRaw({expression});");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        var text = await diagnostic.Location.SourceTree!.GetTextAsync();

        Assert.Equal(expression, text.ToString(diagnostic.Location.SourceSpan));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(UnsafeRawSqlAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains("FromSqlRaw", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("interpolation", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("SQL injection", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact], StringComparison.Ordinal);
        Assert.Contains("allow-list", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Equal("EFD012", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
    }

    [Fact]
    public async Task HonorsPragmaSuppression()
    {
        var source = CaseSource("""
            #pragma warning disable EFD012 // Reviewed: fragment selected from a fixed allow-list.
            static object Run(TestContext context, int id) => context.Entities.FromSqlRaw($"SELECT {id}");
            #pragma warning restore EFD012
            """);

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task HonorsEditorConfigurationSuppression()
    {
        var source = CaseSource("static object Run(TestContext context, int id) => context.Entities.FromSqlRaw($\"SELECT {id}\");");

        Assert.Empty(await AnalyzeAsync(source, configuredSeverity: ReportDiagnostic.Suppress));
    }

    [Fact]
    public async Task HonorsSuppressMessageAttribute()
    {
        var source = CaseSource("""
            [SuppressMessage("Security", "EFD012", Justification = "Fragment selected from a fixed allow-list.")]
            static object Run(TestContext context, int id) => context.Entities.FromSqlRaw($"SELECT {id}");
            """);

        Assert.Empty(await AnalyzeAsync(source));
    }

    private static object[] Case(string member, string additionalTypes = "", int expectedCount = 1) =>
        new object[] { CaseSource(member, additionalTypes), expectedCount };

    private static object[] Negative(string member, string additionalTypes = "") =>
        new object[] { CaseSource(member, additionalTypes), 0 };

    private static string CaseSource(string member, string additionalTypes = "") => $$"""
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class TestContext : DbContext
        {
            public DbSet<Entity> Entities => Set<Entity>();
        }

        sealed class Entity { public int Id { get; set; } }
        {{additionalTypes}}

        static class Subject
        {
            {{member}}
        }
        """;

    private static string GeneratedSource(string member) => "// <auto-generated />\n" + CaseSource(member);

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        ReportDiagnostic? configuredSeverity = null) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: configuredSeverity,
            analyzer: new UnsafeRawSqlAnalyzer(),
            diagnosticId: UnsafeRawSqlAnalyzer.DiagnosticId);
}
