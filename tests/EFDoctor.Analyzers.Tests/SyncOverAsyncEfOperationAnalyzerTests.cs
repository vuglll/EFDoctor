using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class SyncOverAsyncEfOperationAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(TestContext context) => context.Entities.ToListAsync().Result;");
        yield return Case("static void Run(TestContext context) => context.Entities.ToListAsync().Wait();");
        yield return Case("static object Run(TestContext context) => context.Entities.FirstAsync().GetAwaiter().GetResult();");
        yield return Case("static int Run(TestContext context) => context.Entities.CountAsync().ConfigureAwait(false).GetAwaiter().GetResult();");
        yield return Case("static int Run(TestContext context) => context.Entities.CountAsync().ConfigureAwait(true).GetAwaiter().GetResult();");
        yield return Case("static object Run(TestContext context) => EntityFrameworkQueryableExtensions.ToArrayAsync(context.Entities).Result;");
        yield return Case("static int Run(TestContext context) => context.SaveChangesAsync().Result;");
        yield return Case("static void Run(TestContext context) => context.SaveChangesAsync().Wait();");
        yield return Case("static object? Run(TestContext context) => context.Entities.FindAsync(1).Result;");
        yield return Case("static object? Run(TestContext context) => context.Entities.FindAsync(1).GetAwaiter().GetResult();");
        yield return Case("static object Run(DerivedContext context) => context.Entities.SingleAsync().Result;", "sealed class DerivedContext : TestContext { }");
        yield return Case("static object Run(TestContext context) => ((context.Entities.ToListAsync())).Result;");
        yield return Case("static int Run(TestContext context) { context.Entities.AnyAsync().Wait(); return context.Entities.CountAsync().Result; }", expectedCount: 2);
        yield return Case("static int Run(OverridingContext context) => context.SaveChangesAsync().Result;", "sealed class OverridingContext : TestContext { public override Task<int> SaveChangesAsync(CancellationToken token = default) => base.SaveChangesAsync(token); }");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static async Task<object> Run(TestContext context) => await context.Entities.ToListAsync();");
        yield return Negative("static object Run(TestContext context) { var task = context.Entities.ToListAsync(); return task.Result; }");
        yield return Negative("static void Run(TestContext context) => context.Entities.ToListAsync().Wait(100);");
        yield return Negative("static void Run(TestContext context, CancellationToken token) => context.Entities.ToListAsync().Wait(token);");
        yield return Negative("static object Run() => Task.FromResult(1).Result;");
        yield return Negative("static object Run(Store store) => store.ToListAsync().Result;", "sealed class Store { public Task<List<int>> ToListAsync() => Task.FromResult(new List<int>()); }");
        yield return Negative("static object Run(Store store) => store.Result;", "sealed class Store { public object Result => new(); }");
        yield return Negative("static void Run(Store store) => store.Wait();", "sealed class Store { public void Wait() { } }");
        yield return Negative("static object Run(Store store) => store.GetAwaiter().GetResult();", "sealed class Store { public Store GetAwaiter() => this; public object GetResult() => new(); }");
        yield return Negative("static Task<object> Run(TestContext context) => Task.WhenAll<object>(context.Entities.ToListAsync());");
        yield return Negative("static Task<List<Entity>> Run(TestContext context) => context.Entities.ToListAsync();");
        yield return Negative("static int Run(TestContext context) { /* context.SaveChangesAsync().Result */ return 0; }");
        yield return Negative("static string Run() => \"context.SaveChangesAsync().Result\";");
        yield return Negative("static int Run(TestContext context) { #if NEVER return context.SaveChangesAsync().Result; #else return 0; #endif }");
        yield return new object[] { GeneratedSource("static int Run(TestContext context) => context.SaveChangesAsync().Result;"), 0 };
        yield return Negative("static object Run(dynamic context) => context.SaveChangesAsync().Result;");
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsPositiveCases(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Count(static diagnostic => diagnostic.Id == SyncOverAsyncEfOperationAnalyzer.DiagnosticId));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportNegativeCases(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Count(static diagnostic => diagnostic.Id == SyncOverAsyncEfOperationAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task ReportsCompleteBlockingExpressionAndActionableProperties()
    {
        const string expression = "context.Entities.CountAsync().ConfigureAwait(false).GetAwaiter().GetResult()";
        var source = CaseSource($"static int Run(TestContext context) => {expression};");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        var text = await diagnostic.Location.SourceTree!.GetTextAsync();

        Assert.Equal(expression, text.ToString(diagnostic.Location.SourceSpan));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(SyncOverAsyncEfOperationAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains("CountAsync", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("GetAwaiter", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("thread-pool starvation", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact], StringComparison.Ordinal);
        Assert.Contains("Await", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Equal("EFD011", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
    }

    [Fact]
    public async Task HonorsPragmaSuppression()
    {
        var source = CaseSource("""
            #pragma warning disable EFD011 // Reviewed: legacy synchronous integration boundary.
            static int Run(TestContext context) => context.SaveChangesAsync().Result;
            #pragma warning restore EFD011
            """);

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task HonorsEditorConfigurationSuppression()
    {
        var source = CaseSource("static int Run(TestContext context) => context.SaveChangesAsync().Result;");

        Assert.Empty(await AnalyzeAsync(source, configuredSeverity: ReportDiagnostic.Suppress));
    }

    [Fact]
    public async Task HonorsSuppressMessageAttribute()
    {
        var source = CaseSource("""
            [SuppressMessage("Performance", "EFD011", Justification = "Legacy synchronous integration boundary.")]
            static int Run(TestContext context) => context.SaveChangesAsync().Result;
            """);

        Assert.Empty(await AnalyzeAsync(source));
    }

    private static object[] Case(string member, string additionalTypes = "", int expectedCount = 1) =>
        new object[] { CaseSource(member, additionalTypes), expectedCount };

    private static object[] Negative(string member, string additionalTypes = "") =>
        new object[] { CaseSource(member, additionalTypes), 0 };

    private static string CaseSource(string member, string additionalTypes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        class TestContext : DbContext
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
            configuredSeverity: configuredSeverity,
            analyzer: new SyncOverAsyncEfOperationAnalyzer(),
            diagnosticId: SyncOverAsyncEfOperationAnalyzer.DiagnosticId);
}
