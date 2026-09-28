using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFDoctor.Analyzers.Tests;

public sealed class UnawaitedAsyncEfOperationAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static async Task Run(TestContext context) { context.SaveChangesAsync(); await Task.Yield(); }");
        yield return Case("static void Run(TestContext context) { context.SaveChangesAsync(); }");
        yield return Case("static void Run(TestContext context) { _ = context.SaveChangesAsync(); }");
        yield return Case("static void Run(TestContext context) { _ = (context.SaveChangesAsync()); }");
        yield return Case("static void Run(TestContext context) { context.SaveChangesAsync().ConfigureAwait(false); }");
        yield return Case("static void Run(TestContext? context) { context?.SaveChangesAsync(); }");
        yield return Case("static void Run(TestContext context, List<Entity> items) => items.ForEach(item => context.Entities.AddAsync(item));");
        yield return Case("static void Run(TestContext context) { context.Entities.ToListAsync(); }");
        yield return Case("static void Run(TestContext context) { EntityFrameworkQueryableExtensions.FirstAsync(context.Entities); }");
        yield return Case("static void Run(TestContext context) { context.Entities.FindAsync(1); }");
        yield return Case("static void Run(TestContext context) { context.AddAsync(new Entity()); }");
        yield return Case("static void Run(TestContext context) { context.Entities.AddRangeAsync(new Entity()); }");
        yield return Case("static void Run(TestContext context) { context.Entities.Where(entity => !entity.Active).ExecuteDeleteAsync(); }");
        yield return Case("static void Run(TestContext context) { context.Entities.ExecuteUpdateAsync(setters => setters.SetProperty(entity => entity.Active, false)); }");
        yield return Case("static void Run(TestContext context) { context.Database.ExecuteSqlRawAsync(\"DELETE FROM Entities\"); }");
        yield return Case("static void Run(OverridingContext context) { context.SaveChangesAsync(); }", "sealed class OverridingContext : TestContext { public override Task<int> SaveChangesAsync(CancellationToken token = default) => base.SaveChangesAsync(token); }");
        yield return Case("static void Run(TestContext context) { context.Entities.AnyAsync(); _ = context.SaveChangesAsync(); }", expectedCount: 2);
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static async Task Run(TestContext context) => await context.SaveChangesAsync();");
        yield return Negative("static async Task Run(TestContext context) => await context.SaveChangesAsync().ConfigureAwait(false);");
        yield return Negative("static Task<int> Run(TestContext context) => context.SaveChangesAsync();");
        yield return Negative("static Task<int> Run(TestContext context) { var task = context.SaveChangesAsync(); return task; }");
        yield return Negative("static Task? Pending; static void Run(TestContext context) { Pending = context.SaveChangesAsync(); }");
        yield return Negative("static Task Run(TestContext context) => Task.WhenAll(context.SaveChangesAsync(), context.Entities.ToListAsync());");
        yield return Negative("static void Run(TestContext context) { context.SaveChangesAsync().ContinueWith(task => Console.WriteLine(task.Status)); }");
        yield return Negative("static void Run(TestContext context) { context.SaveChangesAsync().Wait(); }");
        yield return Negative("static void Run(TestContext context) { _ = context.SaveChangesAsync().Result; }");
        yield return Negative("static void Run(TestContext context) { context.SaveChangesAsync().GetAwaiter().GetResult(); }");
        yield return Negative("static Func<Task<int>> Run(TestContext context) => () => context.SaveChangesAsync();");
        yield return Negative("static void Run(TestContext context) { Observe(context.SaveChangesAsync()); } static void Observe(Task task) { }");
        yield return Negative("static void Run(TestContext context) { context.SaveChanges(); }");
        yield return Negative("static void Run(TestContext context) { context.Entities.AsAsyncEnumerable(); }");
        yield return Negative("static void Run(Store store) { store.SaveChangesAsync(); _ = store.ToListAsync(); store.AddAsync(); }", "sealed class Store { public Task SaveChangesAsync() => Task.CompletedTask; public Task ToListAsync() => Task.CompletedTask; public Task AddAsync() => Task.CompletedTask; }");
        yield return Negative("static void Run(dynamic context) { context.SaveChangesAsync(); }");
        yield return Negative("static void Run(TestContext context) { /* context.SaveChangesAsync(); */ }");
        yield return Negative("static string Run() => \"context.SaveChangesAsync();\";");
        yield return Negative("""
            static void Run(TestContext context)
            {
            #if NEVER
                context.SaveChangesAsync();
            #endif
            }
            """);
        yield return new object[] { "// <auto-generated/>\n" + Source("static void Run(TestContext context) { context.SaveChangesAsync(); }") };
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsDiscardedEfTasks(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal(UnawaitedAsyncEfOperationAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
            Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
            Assert.Equal("EFD018", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
            Assert.Contains("resolved EF Core operation", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
            Assert.Contains("exceptions are never observed", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact], StringComparison.Ordinal);
            Assert.Contains("Await the EF Core operation", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
            Assert.Contains("separately scoped context", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public void PositiveCasesCompileWithoutErrors(string source, int expectedCount)
    {
        _ = expectedCount;
        var errors = AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.Empty(errors);
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportObservedBlockedOrUnrelatedTasks(string source)
    {
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Theory]
    [InlineData("context.SaveChangesAsync();", "context.SaveChangesAsync()", "as a standalone statement")]
    [InlineData("_ = context.SaveChangesAsync();", "_ = context.SaveChangesAsync()", "through an explicit discard assignment")]
    [InlineData("context.SaveChangesAsync().ConfigureAwait(false);", "context.SaveChangesAsync().ConfigureAwait(false)", "as a standalone statement")]
    [InlineData("context?.SaveChangesAsync();", "context?.SaveChangesAsync()", "as a standalone statement")]
    [InlineData("Action action = () => context.SaveChangesAsync();", "context.SaveChangesAsync()", "as the body of a void-returning lambda")]
    public async Task ReportsCompleteDiscardedExpressionAndForm(string statement, string expectedText, string expectedForm)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source($"static void Run(TestContext? context) {{ {statement} }}")));

        Assert.Equal(expectedText, Text(diagnostic));
        Assert.Contains(expectedForm, diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("SaveChangesAsync", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainsThatConfigureAwaitDoesNotAwait()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source("static void Run(TestContext context) { context.Entities.ToListAsync().ConfigureAwait(false); }")));

        Assert.Contains("ToListAsync", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("ConfigureAwait only configures a later await", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticAndReducedSyntaxProduceEquivalentEvidence()
    {
        var reduced = Assert.Single(await AnalyzeAsync(Source("static void Run(TestContext context) { context.Entities.FirstAsync(); }")));
        var @static = Assert.Single(await AnalyzeAsync(Source("static void Run(TestContext context) { EntityFrameworkQueryableExtensions.FirstAsync(context.Entities); }")));

        Assert.Equal(reduced.Properties[DiagnosticPropertyNames.Evidence], @static.Properties[DiagnosticPropertyNames.Evidence]);
    }

    [Fact]
    public async Task NeverReportsTheSameExpressionAsEfd011()
    {
        var source = Source("""
            static void Blocked(TestContext context) { context.SaveChangesAsync().Wait(); _ = context.Entities.CountAsync().Result; }
            static void Discarded(TestContext context) { context.SaveChangesAsync(); }
            """);

        var diagnostics = await AnalyzeWithAsync(source, new SyncOverAsyncEfOperationAnalyzer(), new UnawaitedAsyncEfOperationAnalyzer());

        Assert.Equal(2, diagnostics.Count(static diagnostic => diagnostic.Id == SyncOverAsyncEfOperationAnalyzer.DiagnosticId));
        var discarded = Assert.Single(diagnostics, static diagnostic => diagnostic.Id == UnawaitedAsyncEfOperationAnalyzer.DiagnosticId);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == SyncOverAsyncEfOperationAnalyzer.DiagnosticId && diagnostic.Location.SourceSpan.IntersectsWith(discarded.Location.SourceSpan));
    }

    [Fact]
    public async Task HonorsPragmaSuppressionWhileSiblingStillReports()
    {
        var source = Source("""
            static void Run(TestContext context)
            {
            #pragma warning disable EFD018 // Reviewed: best-effort audit write on a dedicated context.
                context.SaveChangesAsync();
            #pragma warning restore EFD018
                context.Entities.ToListAsync();
            }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Equal("context.Entities.ToListAsync()", Text(diagnostic));
    }

    [Fact]
    public async Task HonorsEditorConfigSuppression()
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            Source("static void Run(TestContext context) { context.SaveChangesAsync(); }"),
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new UnawaitedAsyncEfOperationAnalyzer(),
            diagnosticId: UnawaitedAsyncEfOperationAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task HonorsSuppressMessageAttributeWhileSiblingStillReports()
    {
        var source = Source("""
            [SuppressMessage("Correctness", "EFD018", Justification = "Best-effort audit write on a dedicated context.")]
            static void Suppressed(TestContext context) { context.SaveChangesAsync(); }
            static void Reported(TestContext context) { context.SaveChangesAsync(); }
            """);

        Assert.Single(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task DoesNotReportWithoutEntityFrameworkReference()
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            "using System.Threading.Tasks; static class Subject { static void Run() { Task.Delay(1); } }",
            includeEntityFramework: false,
            analyzer: new UnawaitedAsyncEfOperationAnalyzer(),
            diagnosticId: UnawaitedAsyncEfOperationAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void DescriptorUsesCorrectnessWarningMetadata()
    {
        Assert.Equal("EFD018", UnawaitedAsyncEfOperationAnalyzer.Rule.Id);
        Assert.Equal("Correctness", UnawaitedAsyncEfOperationAnalyzer.Rule.Category);
        Assert.Equal(DiagnosticSeverity.Warning, UnawaitedAsyncEfOperationAnalyzer.Rule.DefaultSeverity);
        Assert.True(UnawaitedAsyncEfOperationAnalyzer.Rule.IsEnabledByDefault);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new UnawaitedAsyncEfOperationAnalyzer(),
            diagnosticId: UnawaitedAsyncEfOperationAnalyzer.DiagnosticId);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeWithAsync(string source, params DiagnosticAnalyzer[] analyzers)
    {
        var compilation = AnalyzerTestHarness.CreateCompilation(source, true, true, false, false, false, null, UnawaitedAsyncEfOperationAnalyzer.DiagnosticId);
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
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Entity
        {
            public int Id { get; set; }
            public bool Active { get; set; }
        }

        class TestContext : DbContext
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
