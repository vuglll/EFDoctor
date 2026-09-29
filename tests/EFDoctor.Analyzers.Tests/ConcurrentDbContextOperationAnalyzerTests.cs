using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using EFDoctor.Analyzers;

namespace EFDoctor.Analyzers.Tests;

public sealed class ConcurrentDbContextOperationAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static Task Run(AppContext db) => Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync());", 1);
        yield return Case("static Task Run(AppContext db) => Task.WhenAny(db.Orders.ToListAsync(), db.Customers.ToListAsync(), db.Orders.CountAsync());", 2);
        yield return Case("static Task Run(AppContext db) => Task.WhenAll(new Task[] { db.Orders.CountAsync(), db.SaveChangesAsync() });", 1);
        yield return Case("static Task Run(AppContext db) => Task.WhenAll([db.Orders.CountAsync(), db.Customers.CountAsync()]);", 1);
        yield return Case("static async Task Run(IDbContextFactory<AppContext> factory) { await using var db = factory.CreateDbContext(); await Task.WhenAll(db.Orders.ToListAsync(), db.Set<Customer>().ToListAsync()); }", 1);
        yield return Case("Task Run() => Task.WhenAll(_db.Orders.ToListAsync(), _db.Customers.ToListAsync());", 1);
        yield return Case("Task Run() => Task.WhenAll(Db.Orders.ToListAsync(), Db.Customers.ToListAsync());", 1);
        yield return Case("static async Task Run(AppContext db) { var orders = db.Orders.ToListAsync(); var customers = db.Customers.ToListAsync(); await Task.WhenAll(orders, customers); }", 1);
        yield return Case("static async Task<int> Run(AppContext db) { var orders = db.Orders.ToListAsync(); var count = await db.Customers.CountAsync(); return (await orders).Count + count; }", 1);
        yield return Case("static async Task Run(AppContext db) { var order = db.Orders.FindAsync(1); await db.SaveChangesAsync(); await order; }", 1);
        yield return Case("static Task Run(AppContext db, int[] ids) => Task.WhenAll(ids.Select(async id => await db.Orders.FindAsync(id)));", 1);
        yield return Case("static async Task Run(AppContext db, int[] ids) { var tasks = ids.Select(id => db.Orders.FirstAsync(o => o.Id == id)).ToList(); await Task.WhenAll(tasks); }", 1);
        yield return Case("static Task Run(AppContext db) { var query = db.Orders.Where(o => o.Id > 0); return Task.WhenAll(query.ToListAsync(), db.Customers.ToListAsync()); }", 1);
        yield return ContextCase("public Task Both() => Task.WhenAll(Orders.ToListAsync(), SaveChangesAsync());", 1);
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Case("static async Task Run(AppContext db) { await db.Orders.ToListAsync(); await db.Customers.ToListAsync(); }");
        yield return Case("static async Task Run(IDbContextFactory<AppContext> factory) { using var first = factory.CreateDbContext(); using var second = factory.CreateDbContext(); await Task.WhenAll(first.Orders.ToListAsync(), second.Orders.ToListAsync()); }");
        yield return Case("static Task Run(AppContext db) => Task.WhenAll(db.Orders.ToListAsync(), Task.Delay(1));");
        yield return Case("static async Task Run(IDbContextFactory<AppContext> factory) { var db = factory.CreateDbContext(); await db.Orders.CountAsync(); db = factory.CreateDbContext(); await Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync()); }");
        yield return Case("static Task Run(AppContext db, AppContext other) { db = other; return Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync()); }");
        yield return Case("Task Run() => Task.WhenAll(Fresh.Orders.ToListAsync(), Fresh.Customers.ToListAsync());");
        yield return Case("static Task Run() => Task.WhenAll(Create().Orders.ToListAsync(), Create().Customers.ToListAsync());");
        yield return Case("static Task Run(Service holder) => Task.WhenAll(holder.Db.Orders.ToListAsync(), holder.Db.Customers.ToListAsync());");
        yield return Case("static Task Run(DbSet<Order> orders) => Task.WhenAll(orders.ToListAsync(), orders.CountAsync());");
        yield return Case("static async Task Run(AppContext db) { var orders = db.Orders.ToListAsync(); await orders; await db.Customers.CountAsync(); }");
        yield return Case("static async Task Run(AppContext db) { var orders = db.Orders.ToListAsync(); Observe(orders); await db.Customers.CountAsync(); await orders; }");
        yield return Case("static async Task Run(AppContext db) { var orders = db.Orders.ToListAsync(); Func<Task<int>> later = () => db.Customers.CountAsync(); await orders; await later(); }");
        yield return Case("static async Task Run(IDbContextFactory<AppContext> first, IDbContextFactory<AppContext> second) { await using var a = first.CreateDbContext(); await using var b = second.CreateDbContext(); var orders = a.Orders.ToListAsync(); await b.Customers.CountAsync(); await orders; }");
        yield return Case("static Task Run(IDbContextFactory<AppContext> factory, int[] ids) => Task.WhenAll(ids.Select(async id => { await using var own = factory.CreateDbContext(); return await own.Orders.FindAsync(id); }));");
        yield return Case("static Task Run(int[] ids) => Task.WhenAll(ids.Select(id => Task.FromResult(id)));");
        yield return Case("static Task Run(AppContext db) => Combinators.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync());");
        yield return Case("static Task Run(AppContext db, int[] ids) => Task.WhenAll(Lookalike.Select(ids, id => db.Orders.FirstAsync(o => o.Id == id)));");
        yield return Case("static Task Run(AppContext db) => Task.WhenAll(db.Orders.ToListAsync());");
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Context through a DbSet property")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Context through Set and SaveChangesAsync")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Injected context field")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Two queries in Task.WhenAll")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Three operations in Task.WhenAny")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Array or collection expression argument")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Two task locals awaited together")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Task local awaited after a later operation")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Async selector over a captured context")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Selected tasks stored in a local")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Analyzer fixture suite")]
    public async Task ReportsPositiveFixture(string source, int expectedCount)
    {
        var matches = await AnalyzeAsync(source);
        Assert.Equal(expectedCount, matches.Count(static diagnostic => diagnostic.Id == ConcurrentDbContextOperationAnalyzer.DiagnosticId));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Different context instances")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Context that cannot be compared")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Different contexts in one combinator")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Task local awaited before the next operation")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Task local referenced before the next operation")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Later operation inside a lambda")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Context created inside the selector")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Sequential awaits")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Unrelated asynchronous work in a combinator")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Look-alike methods")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Analyzer fixture suite")]
    public async Task DoesNotReportNegativeFixture(string source)
    {
        var matches = await AnalyzeAsync(source);
        Assert.DoesNotContain(matches, static diagnostic => diagnostic.Id == ConcurrentDbContextOperationAnalyzer.DiagnosticId);
    }

    [Fact]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Exact diagnostic location")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Qualified remediation")]
    public async Task ReportsSecondCombinatorArgumentWithCompleteProperties()
    {
        var source = QuerySource("static Task Run(AppContext db) => Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync());");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("db.Customers.ToListAsync()", TextOf(diagnostic));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("This EF Core operation starts while another operation on the same DbContext is still pending", diagnostic.GetMessage());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD027", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal(ConcurrentDbContextOperationAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Equal(ConcurrentDbContextOperationAnalyzer.Remediation, diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]);
        Assert.Contains("runs on context 'db', and is passed to Task.WhenAll after 'db.Orders.ToListAsync()'", diagnostic.Properties[DiagnosticPropertyNames.Evidence]!, StringComparison.Ordinal);
        Assert.Contains("Await each operation", ConcurrentDbContextOperationAnalyzer.Remediation, StringComparison.Ordinal);
        Assert.Contains("IDbContextFactory<TContext>", ConcurrentDbContextOperationAnalyzer.Remediation, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Three operations in Task.WhenAny")]
    public async Task ReportsEveryLaterCombinatorArgument()
    {
        var source = QuerySource("static Task Run(AppContext db) => Task.WhenAny(db.Orders.ToListAsync(), db.Customers.ToListAsync(), db.Orders.CountAsync());");

        var texts = (await AnalyzeAsync(source)).Select(TextOf).OrderBy(static text => text, StringComparer.Ordinal).ToArray();

        Assert.Equal(["db.Customers.ToListAsync()", "db.Orders.CountAsync()"], texts);
    }

    [Fact]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Two task locals awaited together")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Injected context field")]
    public async Task TaskLocalEvidenceNamesTheLocalAndField()
    {
        var source = QuerySource("async Task Run() { var orders = _db.Orders.ToListAsync(); var customers = _db.Customers.ToListAsync(); await Task.WhenAll(orders, customers); }");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("_db.Customers.ToListAsync()", TextOf(diagnostic));
        Assert.Contains("runs on context 'this._db', and starts while task local 'orders', initialized by '_db.Orders.ToListAsync()'", diagnostic.Properties[DiagnosticPropertyNames.Evidence]!, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Async selector over a captured context")]
    public async Task ProjectionEvidenceNamesTheCapturedContext()
    {
        var source = QuerySource("static Task Run(AppContext db, int[] ids) => Task.WhenAll(ids.Select(async id => await db.Orders.FindAsync(id)));");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("db.Orders.FindAsync(id)", TextOf(diagnostic));
        Assert.Contains("captured from outside the selector of an Enumerable.Select passed to Task.WhenAll", diagnostic.Properties[DiagnosticPropertyNames.Evidence]!, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Pragma suppression")]
    public async Task HonorsPragmaSuppressionWhileSiblingStillReports()
    {
        var source = QuerySource("""
            #pragma warning disable EFD027 // Reviewed: test double with a thread-safe context.
            static Task Suppressed(AppContext db) => Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync());
            #pragma warning restore EFD027
            static Task Reported(AppContext db) => Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync());
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", LineOf(diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Editor configuration suppression")]
    public async Task HonorsEditorConfigSuppression()
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            QuerySource("static Task Run(AppContext db) => Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync());"),
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new ConcurrentDbContextOperationAnalyzer(),
            diagnosticId: ConcurrentDbContextOperationAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/SuppressMessage attribute")]
    public async Task HonorsSuppressMessageAttributeWhileSiblingStillReports()
    {
        var source = QuerySource("""
            [SuppressMessage("Reliability", "EFD027", Justification = "Test double with a thread-safe context.")]
            static Task Suppressed(AppContext db) => Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync());
            static Task Reported(AppContext db) => Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync());
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", LineOf(diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Generated or malformed code")]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + QuerySource("static Task Run(AppContext db) => Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync());")));
    }

    [Fact]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Generated or malformed code")]
    public async Task DoesNotReportOrThrowOnUnresolvedCode()
    {
        var source = QuerySource("static Task Run(AppContext db) => Task.WhenAll(db.Orders.ToListAsync(), db.Missing.ToListAsync(), Undefined());");

        Assert.Contains(AnalyzerTestHarness.GetCompilationDiagnostics(source), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Analyzer fixture suite")]
    public void FixturesCompile()
    {
        var failures = PositiveCases().Concat(NegativeCases())
            .Select(static fixture => (string)fixture[0])
            .SelectMany(static source => AnalyzerTestHarness.GetCompilationDiagnostics(source)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => $"{diagnostic.Id} {diagnostic.GetMessage()}"))
            .ToArray();

        Assert.Empty(failures);
        Assert.True(PositiveCases().Count() >= 10);
        Assert.True(NegativeCases().Count() >= 10);
    }

    [Fact]
    public void DescriptorUsesReliabilityWarningMetadata()
    {
        Assert.Equal("EFD027", ConcurrentDbContextOperationAnalyzer.Rule.Id);
        Assert.Equal("Concurrent operations on one DbContext", ConcurrentDbContextOperationAnalyzer.Rule.Title.ToString());
        Assert.Equal("Reliability", ConcurrentDbContextOperationAnalyzer.Rule.Category);
        Assert.Equal(DiagnosticSeverity.Warning, ConcurrentDbContextOperationAnalyzer.Rule.DefaultSeverity);
        Assert.True(ConcurrentDbContextOperationAnalyzer.Rule.IsEnabledByDefault);
    }

    private static string TextOf(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string LineOf(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString();

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            analyzer: new ConcurrentDbContextOperationAnalyzer(),
            diagnosticId: ConcurrentDbContextOperationAnalyzer.DiagnosticId);

    private static object[] Case(string member, int expectedCount) => [QuerySource(member), expectedCount];

    private static object[] Case(string member) => [QuerySource(member)];

    private static object[] ContextCase(string contextMember, int expectedCount) => [QuerySource(string.Empty, contextMember), expectedCount];

    private static string QuerySource(string member, string contextMember = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Order
        {
            public int Id { get; set; }
        }

        sealed class Customer
        {
            public int Id { get; set; }
        }

        sealed class AppContext : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();
            public DbSet<Customer> Customers => Set<Customer>();
            {{contextMember}}
        }

        static class Combinators
        {
            public static Task WhenAll(params Task[] tasks) => Task.CompletedTask;
        }

        static class Lookalike
        {
            public static IEnumerable<Task<Order>> Select(int[] ids, Func<int, Task<Order>> selector) => Array.Empty<Task<Order>>();
        }

        sealed class Service
        {
            private readonly AppContext _db;

            public Service(AppContext db)
            {
                _db = db;
                Db = db;
            }

            public AppContext Db { get; }

            private AppContext Fresh => new AppContext();

            static AppContext Create() => new AppContext();

            static void Observe(Task task)
            {
            }

            {{member}}
        }
        """;
}
