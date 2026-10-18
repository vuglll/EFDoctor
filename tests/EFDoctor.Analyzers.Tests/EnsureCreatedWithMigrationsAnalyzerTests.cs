using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class EnsureCreatedWithMigrationsAnalyzerTests
{
    private const string ShopMigration = """
        [DbContext(typeof(ShopContext))]
        [Migration("20260101000000_Initial")]
        sealed class Initial : Migration
        {
            protected override void Up(MigrationBuilder migrationBuilder) { }
        }
        """;

    private const string ShopSnapshot = """
        [DbContext(typeof(ShopContext))]
        sealed class ShopContextModelSnapshot : ModelSnapshot
        {
            protected override void BuildModel(ModelBuilder modelBuilder) { }
        }
        """;

    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static void Run(ShopContext db) => db.Database.EnsureCreated();", ShopMigration, "migration class 'Initial'");
        yield return Case("static Task<bool> Run(ShopContext db) => db.Database.EnsureCreatedAsync();", ShopMigration, "migration class 'Initial'");
        yield return Case("static async Task Run(ShopContext db) { await db.Database.EnsureCreatedAsync(CancellationToken.None); }", ShopSnapshot, "model snapshot class 'ShopContextModelSnapshot'");
        yield return Case("static void Run(ShopContext db) { if (db.Database.EnsureCreated()) { Console.WriteLine(\"created\"); } }", ShopSnapshot, "model snapshot class");
        yield return Case("static void Run(TenantShopContext db) => db.Database.EnsureCreated();", ShopMigration, "this project manages 'ShopContext' with migrations");
        yield return Case("static void Run(ShopContext db) => db.Database.EnsureCreated();\nstatic void Upgrade(ShopContext db) => db.Database.Migrate();", "", "call to 'db.Database.Migrate()'");
        yield return Case("static void Run(ShopContext db) => db.Database.EnsureCreated();\nstatic Task Upgrade(ShopContext db) => db.Database.MigrateAsync();", "", "call to 'db.Database.MigrateAsync()'");
        yield return Case("static void Run(IServiceProvider services) { using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ShopContext>(); db.Database.EnsureCreated(); }", ShopMigration, "migration class");
        yield return Case("static void Run(ShopContext db) { db.Database.EnsureDeleted(); db.Database.EnsureCreated(); }", ShopMigration, "migration class");
        yield return Case("static void Run(ShopContext db, bool fresh) { if (fresh) { db.Database.EnsureCreated(); } else { Console.WriteLine(\"existing\"); } }", ShopMigration, "migration class");
        yield return Case("static void Run(ShopContext db) => RelationalDatabaseFacadeExtensions.Migrate(db.Database);\nstatic void Seed(ShopContext db) => db.Database.EnsureCreated();", "", "call to");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static void Run(ShopContext db) => db.Database.EnsureCreated();", "");
        yield return Negative("static void Run(CacheContext db) => db.Database.EnsureCreated();", ShopMigration);
        yield return Negative("static void Run(CacheContext db) => db.Database.EnsureCreated();\nstatic void Upgrade(ShopContext db) => db.Database.Migrate();", "");
        yield return Negative("static void Run(ShopContext db) => db.Database.Migrate();", ShopMigration);
        yield return Negative("static void Run(ShopContext db) => db.Database.EnsureDeleted();", ShopMigration);
        yield return Negative("static void Run(Store store) => store.EnsureCreated();", ShopMigration + "\nsealed class Store { public void EnsureCreated() { } }");
        yield return Negative("static void Run(ShopContext db) => db.Database.CanConnect();", ShopMigration);
        yield return Negative("static void Run(ShopContext db) => db.Database.EnsureCreated();", "[DbContext(typeof(CacheContext))]\nsealed class CacheSnapshot : ModelSnapshot { protected override void BuildModel(ModelBuilder modelBuilder) { } }");
        yield return Negative("static void Run(ShopContext db) => db.Database.EnsureCreated();", "sealed class NotAMigration { }");
        yield return Negative("static void Run(CacheContext db, ShopContext shop) { db.Database.EnsureCreated(); shop.Database.Migrate(); }", "");
        yield return Negative("static async Task Run(ShopContext db, bool sqlite) { if (sqlite) { await db.Database.EnsureCreatedAsync(); } else { await db.Database.MigrateAsync(); } }", "");
        yield return Negative("static void Run(ShopContext db, bool sqlite) { if (sqlite) db.Database.EnsureCreated(); else db.Database.Migrate(); }", ShopMigration);
        yield return Negative("static void Run(ShopContext db, bool sqlite) { if (!sqlite) { db.Database.Migrate(); } else { Console.WriteLine(\"sqlite\"); db.Database.EnsureCreated(); } }", ShopMigration);
    }

    [Theory]
    [Trait("Spec", "efd034-ensurecreated-with-migrations/EnsureCreated next to migrations")]
    [Trait("Spec", "efd034-ensurecreated-with-migrations/Async form and model snapshot")]
    [Trait("Spec", "efd034-ensurecreated-with-migrations/Migrate called elsewhere")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsEnsureCreatedOnAMigratedContext(string source, string evidence)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Contains(evidence, diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.StartsWith("db.Database.EnsureCreated", Text(diagnostic), StringComparison.Ordinal);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "efd034-ensurecreated-with-migrations/Context without migrations")]
    [Trait("Spec", "efd034-ensurecreated-with-migrations/Project without migrations")]
    [Trait("Spec", "efd034-ensurecreated-with-migrations/Unrelated EnsureCreated method")]
    [Trait("Spec", "efd034-ensurecreated-with-migrations/Provider switch")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportContextsWithoutMigrations(string source)
    {
        Assert.DoesNotContain(AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd034-ensurecreated-with-migrations/Complete structured finding")]
    public async Task FindingCarriesTheCompleteContract()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source("static void Run(ShopContext db) => db.Database.EnsureCreated();", ShopMigration)));

        Assert.Equal(EnsureCreatedWithMigrationsAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("Correctness", diagnostic.Descriptor.Category);
        Assert.Equal("EnsureCreated bypasses the migrations of 'ShopContext'", diagnostic.GetMessage());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD034", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal("'db.Database.EnsureCreated()' runs on a 'ShopContext', and this project manages 'ShopContext' with migrations: the migration class 'Initial'.", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Equal(EnsureCreatedWithMigrationsAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("Database.Migrate()", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd034-ensurecreated-with-migrations/Suppression")]
    public async Task HonorsPragmaSuppressMessageAndEditorConfig()
    {
        var source = Source("""
            static void Pragma(ShopContext db)
            {
            #pragma warning disable EFD034
                db.Database.EnsureCreated();
            #pragma warning restore EFD034
            }

            [SuppressMessage("Correctness", "EFD034", Justification = "Local demo database.")]
            static void Attributed(ShopContext db) => db.Database.EnsureCreated();

            static void Reported(ShopContext db) => db.Database.EnsureCreated();
            """, ShopMigration);

        Assert.Single(await AnalyzeAsync(source));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new EnsureCreatedWithMigrationsAnalyzer(),
            diagnosticId: EnsureCreatedWithMigrationsAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + Source("static void Run(ShopContext db) => db.Database.EnsureCreated();", ShopMigration)));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new EnsureCreatedWithMigrationsAnalyzer(),
            diagnosticId: EnsureCreatedWithMigrationsAnalyzer.DiagnosticId);

    private static object[] Case(string member, string additionalTypes, string evidence) => [Source(member, additionalTypes), evidence];

    private static object[] Negative(string member, string additionalTypes) => [Source(member, additionalTypes)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string Source(string member, string additionalTypes) => $$"""
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Infrastructure;
        using Microsoft.EntityFrameworkCore.Migrations;
        using Microsoft.Extensions.DependencyInjection;

        class ShopContext : DbContext { }
        sealed class TenantShopContext : ShopContext { }
        sealed class CacheContext : DbContext { }

        {{additionalTypes}}

        static class Subject
        {
            {{member}}
        }
        """;
}
