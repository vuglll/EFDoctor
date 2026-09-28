using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using EFDoctor.Analyzers;

namespace EFDoctor.Analyzers.Tests;

public sealed class SyncDatabaseCallInAsyncAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static async Task Run(TestContext context) { var list = context.Entities.Where(entity => entity.Id > 0).ToList(); await Task.CompletedTask; }", 1);
        yield return Case("static async Task Run(TestContext context) { context.SaveChanges(); await Task.CompletedTask; }", 1);
        yield return Case("static async Task Run(TestContext context, int id) { var entity = context.Entities.Find(id); await Task.CompletedTask; }", 1);
        yield return Case("static async Task Run(TestContext context) { var count = context.Entities.Count(); await Task.CompletedTask; }", 1);
        yield return Case("static void Run(TestContext context) { System.Func<Task> f = async () => { var list = context.Entities.ToList(); await Task.CompletedTask; }; System.GC.KeepAlive(f); }", 1);
        yield return Case("static async Task Run(TestContext context, bool flag) { if (flag) { await Task.Delay(1); } else { var list = context.Entities.ToList(); } }", 1);
        yield return Case("static async Task Run(TestContext context, bool flag) { if (flag) { await context.SaveChangesAsync(); } else { System.Func<Task> f = async () => { context.SaveChanges(); await Task.CompletedTask; }; await f(); } }", 1);
        yield return Case("static async Task Run(TestContext context, bool flag) { var first = flag ? await context.Entities.FirstOrDefaultAsync() : context.Entities.FirstOrDefault(); var count = context.Entities.Count(); }", 1);
        yield return Case("static async Task Run(TestContext context) { var query = context.Entities.Where(entity => entity.Id > 0); var list = query.ToList(); await Task.CompletedTask; }", 1);
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Case("static void Run(TestContext context) { var list = context.Entities.ToList(); }");
        yield return Case("static async Task Run(TestContext context) { var list = await context.Entities.ToListAsync(); }");
        yield return Case("static async Task Run() { var list = new System.Collections.Generic.List<Entity>().Where(entity => entity.Id > 0).ToList(); await Task.CompletedTask; }");
        yield return Case("static async Task Run(System.Linq.IQueryable<Entity> query) { var list = query.ToList(); await Task.CompletedTask; }");
        yield return Case("static async Task Run(TestContext context) { System.Action a = () => { var list = context.Entities.ToList(); }; a(); await Task.CompletedTask; }");
        yield return Case("static async Task Run(TestContext context, bool async) { var first = async ? await context.Entities.FirstOrDefaultAsync(entity => entity.Id > 0) : context.Entities.FirstOrDefault(entity => entity.Id > 0); }");
        yield return Case("static async Task Run(TestContext context, bool async) { var list = async ? await context.Entities.Where(entity => entity.Id > 0).ToListAsync().ConfigureAwait(false) : context.Entities.Where(entity => entity.Id > 0).ToList(); }");
        yield return Case("static async Task Run(TestContext context, bool async) { if (async) { await context.SaveChangesAsync().ConfigureAwait(false); } else { context.SaveChanges(); } }");
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    [Trait("Spec", "efd010-sync-db-call-in-async/Synchronous terminal in an async method")]
    [Trait("Spec", "efd010-sync-db-call-in-async/SaveChanges in an async method")]
    [Trait("Spec", "efd010-sync-db-call-in-async/Find in an async method")]
    [Trait("Spec", "efd010-sync-db-call-in-async/Synchronous call in an async lambda")]
    [Trait("Spec", "efd010-sync-db-call-in-async/Other arm awaits something else")]
    [Trait("Spec", "efd010-sync-db-call-in-async/Analyzer fixture suite")]
    public async Task ReportsPositiveFixture(string source, int expectedCount)
    {
        var matches = await AnalyzeAsync(source);
        Assert.Equal(expectedCount, matches.Count(static diagnostic => diagnostic.Id == SyncDatabaseCallInAsyncAnalyzer.DiagnosticId));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    [Trait("Spec", "efd010-sync-db-call-in-async/Synchronous method is not reported")]
    [Trait("Spec", "efd010-sync-db-call-in-async/Already asynchronous call is not reported")]
    [Trait("Spec", "efd010-sync-db-call-in-async/In-memory or non-EF source is not reported")]
    [Trait("Spec", "efd010-sync-db-call-in-async/Sync arm of a sync/async conditional expression")]
    [Trait("Spec", "efd010-sync-db-call-in-async/Sync branch of a sync/async if statement")]
    [Trait("Spec", "efd010-sync-db-call-in-async/Analyzer fixture suite")]
    public async Task DoesNotReportNegativeFixture(string source)
    {
        var matches = await AnalyzeAsync(source);
        Assert.DoesNotContain(matches, static diagnostic => diagnostic.Id == SyncDatabaseCallInAsyncAnalyzer.DiagnosticId);
    }

    [Fact]
    [Trait("Spec", "efd010-sync-db-call-in-async/Complete structured finding")]
    [Trait("Spec", "efd010-sync-db-call-in-async/Exact diagnostic location")]
    [Trait("Spec", "efd010-sync-db-call-in-async/Qualified remediation")]
    public async Task FindingNamesAsyncCounterpartAndCarriesMetadata()
    {
        var source = QuerySource("static async Task Run(TestContext context) { var list = context.Entities.Where(entity => entity.Id > 0).ToList(); await Task.CompletedTask; }");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("medium", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains("ToListAsync", diagnostic.Properties[DiagnosticPropertyNames.Evidence]!, StringComparison.Ordinal);
        Assert.Contains("ToListAsync", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal("EFD010", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal("context.Entities.Where(entity => entity.Id > 0).ToList()", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        Assert.Contains("Await 'ToListAsync'", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]!, StringComparison.Ordinal);
        Assert.Contains("off the hot path", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]!, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd010-sync-db-call-in-async/Pragma suppression")]
    public async Task PragmaSuppressionIsHonored()
    {
        var source = QuerySource("""
            static async Task Run(TestContext context)
            {
            #pragma warning disable EFD010
                var list = context.Entities.ToList();
            #pragma warning restore EFD010
                await Task.CompletedTask;
            }
            """);

        Assert.DoesNotContain(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == SyncDatabaseCallInAsyncAnalyzer.DiagnosticId);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source.Contains("class TestContext", StringComparison.Ordinal) ? source : QuerySource(source),
            analyzer: new SyncDatabaseCallInAsyncAnalyzer(),
            diagnosticId: SyncDatabaseCallInAsyncAnalyzer.DiagnosticId);

    private static object[] Case(string member, int expectedCount) => [QuerySource(member), expectedCount];

    private static object[] Case(string member) => [QuerySource(member)];

    private static string QuerySource(string member) => $$"""
        using System.Collections.Generic;
        using System.Linq;
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

        static class Subject
        {
            {{member}}
        }
        """;
}
