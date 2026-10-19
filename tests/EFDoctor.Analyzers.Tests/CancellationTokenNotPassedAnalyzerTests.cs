using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class CancellationTokenNotPassedAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static Task<List<Order>> Run(ShopContext db, CancellationToken cancellationToken) => db.Orders.ToListAsync();", "db.Orders.ToListAsync()", "ToListAsync", "cancellationToken");
        yield return Case("static Task<Order?> Run(ShopContext db, int id, CancellationToken token) => db.Orders.FirstOrDefaultAsync(o => o.Id == id);", "db.Orders.FirstOrDefaultAsync(o => o.Id == id)", "FirstOrDefaultAsync", "token");
        yield return Case("static Task<int> Run(ShopContext db, CancellationToken ct) => db.SaveChangesAsync();", "db.SaveChangesAsync()", "SaveChangesAsync", "ct");
        yield return Case("static Task<int> Run(ShopContext db, CancellationToken ct) => db.SaveChangesAsync(acceptAllChangesOnSuccess: true);", "db.SaveChangesAsync(acceptAllChangesOnSuccess: true)", "SaveChangesAsync", "ct");
        yield return Case("static ValueTask<Order?> Run(ShopContext db, int id, CancellationToken ct) => db.Orders.FindAsync(id);", "db.Orders.FindAsync(id)", "FindAsync", "ct");
        yield return Case("static Task<int> Run(ShopContext db, CancellationToken ct) => db.Orders.Where(o => !o.Open).ExecuteDeleteAsync();", "db.Orders.Where(o => !o.Open).ExecuteDeleteAsync()", "ExecuteDeleteAsync", "ct");
        yield return Case("static Task<bool> Run(ShopContext db, CancellationToken ct) => EntityFrameworkQueryableExtensions.AnyAsync(db.Orders);", "EntityFrameworkQueryableExtensions.AnyAsync(db.Orders)", "AnyAsync", "ct");
        yield return Case("static Func<CancellationToken, Task<int>> Run(ShopContext db) => async ct => await db.Orders.CountAsync();", "db.Orders.CountAsync()", "CountAsync", "ct");
        yield return Case("static async Task Run(ShopContext db, CancellationToken ct) { async Task<int> Count() => await db.Orders.CountAsync(); await Count(); }", "db.Orders.CountAsync()", "CountAsync", "ct");
        yield return Case("static async Task Run(ShopContext db, CancellationToken stoppingToken) { var orders = await db.Orders.Where(o => o.Open).ToListAsync(); foreach (var order in orders) { order.Open = false; } await db.SaveChangesAsync(stoppingToken); }", "db.Orders.Where(o => o.Open).ToListAsync()", "ToListAsync", "stoppingToken");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static Task<List<Order>> Run(ShopContext db, CancellationToken cancellationToken) => db.Orders.ToListAsync(cancellationToken);");
        yield return Negative("static Task<List<Order>> Run(ShopContext db, CancellationToken cancellationToken) => db.Orders.ToListAsync(CancellationToken.None);");
        yield return Negative("static Task<List<Order>> Run(ShopContext db, CancellationToken cancellationToken) => db.Orders.ToListAsync(default);");
        yield return Negative("static Task<int> Run(ShopContext db, CancellationToken ct) => db.SaveChangesAsync(ct);");
        yield return Negative("static ValueTask<Order?> Run(ShopContext db, int id, CancellationToken ct) => db.Orders.FindAsync(new object[] { id }, ct);");
        yield return Negative("static Task<List<Order>> Run(ShopContext db) => db.Orders.ToListAsync();");
        yield return Negative("static Task<int> Run(ShopContext db) => db.SaveChangesAsync();");
        yield return Negative("static List<Order> Run(ShopContext db, CancellationToken ct) => db.Orders.ToList();");
        yield return Negative("static Task<int> Run(Store store, CancellationToken ct) => store.SaveChangesAsync();");
        yield return Negative("static Task<List<int>> Run(List<int> values, CancellationToken ct) => Task.FromResult(values.ToList());");
        yield return Negative("static Task<int> Run(ShopContext db, CancellationToken ct) => db.Orders.CountAsync(o => o.Open, ct);");
    }

    [Theory]
    [Trait("Spec", "efd016-cancellation-token-not-passed/Query terminal without the token")]
    [Trait("Spec", "efd016-cancellation-token-not-passed/SaveChanges, FindAsync, and bulk operations")]
    [Trait("Spec", "efd016-cancellation-token-not-passed/Token from an enclosing lambda")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsAsyncCallsWithoutTheTokenInScope(string source, string expectedText, string method, string token)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal(expectedText, Text(diagnostic));
        Assert.Equal($"'{method}' doesn't pass the CancellationToken '{token}' that is in scope", diagnostic.GetMessage());
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "efd016-cancellation-token-not-passed/Token passed or explicitly declined")]
    [Trait("Spec", "efd016-cancellation-token-not-passed/No token in scope")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportPassedOrMissingTokens(string source)
    {
        Assert.DoesNotContain(AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd016-cancellation-token-not-passed/Complete structured finding")]
    public async Task FindingCarriesTheCompleteContract()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source("static Task<List<Order>> Run(ShopContext db, CancellationToken cancellationToken) => db.Orders.ToListAsync();")));

        Assert.Equal(CancellationTokenNotPassedAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("Reliability", diagnostic.Descriptor.Category);
        Assert.Equal("advisory", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD016", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Contains("parameter 'cancellationToken' of type CancellationToken is in scope", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Equal(CancellationTokenNotPassedAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("FindAsync(new object[] { key }, cancellationToken)", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd016-cancellation-token-not-passed/Suppression")]
    public async Task HonorsPragmaSuppressMessageAndEditorConfig()
    {
        var source = Source("""
            static Task<List<Order>> Pragma(ShopContext db, CancellationToken ct)
            {
            #pragma warning disable EFD016
                return db.Orders.ToListAsync();
            #pragma warning restore EFD016
            }

            [SuppressMessage("Reliability", "EFD016", Justification = "Must finish even if the caller cancels.")]
            static Task<int> Attributed(ShopContext db, CancellationToken ct) => db.SaveChangesAsync();

            static Task<int> Reported(ShopContext db, CancellationToken ct) => db.Orders.CountAsync();
            """);

        Assert.Equal("db.Orders.CountAsync()", Text(Assert.Single(await AnalyzeAsync(source))));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new CancellationTokenNotPassedAnalyzer(),
            diagnosticId: CancellationTokenNotPassedAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + Source("static Task<List<Order>> Run(ShopContext db, CancellationToken ct) => db.Orders.ToListAsync();")));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new CancellationTokenNotPassedAnalyzer(),
            diagnosticId: CancellationTokenNotPassedAnalyzer.DiagnosticId);

    private static object[] Case(string member, string expectedText, string method, string token) => [Source(member), expectedText, method, token];

    private static object[] Negative(string member) => [Source(member)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string Source(string member) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Order
        {
            public int Id { get; set; }
            public bool Open { get; set; }
        }

        sealed class ShopContext : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();
        }

        sealed class Store
        {
            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        }

        static class Subject
        {
            {{member}}
        }
        """;
}
