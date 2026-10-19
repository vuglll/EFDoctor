using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class UnsizedSnapshotColumnAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("""b.Property<string>("Email").HasColumnType("nvarchar(max)"); b.HasIndex("Email");""", "high", "'Account.Email' is nvarchar(max), which SQL Server cannot use in an index or key");
        yield return Case("""b.Property<string>("Email").IsRequired().HasColumnType("NVARCHAR(MAX)"); b.HasIndex("TenantId", "Email").IsUnique();""", "high", "'Account.Email' is nvarchar(max), which SQL Server cannot use in an index or key");
        yield return Case("""b.Property<string>("Code").HasColumnType("varchar(max)"); b.HasAlternateKey("Code");""", "high", "'Account.Code' is varchar(max), which SQL Server cannot use in an index or key");
        yield return Case("""b.Property<string>("Code").HasColumnType("nvarchar(max)"); b.HasKey("Code");""", "high", "'Account.Code' is nvarchar(max)");
        yield return Case("""b.Property<decimal>("Rate").HasColumnType("decimal(18,2)");""", "advisory", "'Account.Rate' has no configured precision, so SQL Server stores it as decimal(18,2)");
        yield return Case("""b.Property<decimal?>("Discount").HasColumnType("decimal(18,2)");""", "advisory", "'Account.Discount' has no configured precision");
        yield return Case("""b.Property<decimal>("Balance").IsRequired().HasColumnType("decimal(18,2)");""", "advisory", "'Account.Balance' has no configured precision");
        yield return Case("""b.Property<decimal>("Total").HasColumnType("decimal(18,2)").HasAnnotation("Relational:ColumnOrder", 3);""", "advisory", "'Account.Total' has no configured precision");
        yield return Case("""b.Property<string>("Code").HasColumnType("nvarchar(max)"); b.HasKey("TenantId", "Code");""", "high", "'Account.Code' is nvarchar(max)");
        yield return Case("""b.Property<string>("Name").HasColumnType("nvarchar(max)"); b.HasIndex("Name").IsUnique().HasFilter("[Name] IS NOT NULL");""", "high", "'Account.Name' is nvarchar(max)");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("""b.Property<string>("Email").HasColumnType("nvarchar(max)");""");
        yield return Negative("""b.Property<string>("Email").HasMaxLength(256).HasColumnType("nvarchar(256)"); b.HasIndex("Email");""");
        yield return Negative("""b.Property<string>("Email").HasColumnType("text"); b.HasIndex("Email");""");
        yield return Negative("""b.Property<decimal>("Rate").HasPrecision(18, 6).HasColumnType("decimal(18,6)");""");
        yield return Negative("""b.Property<decimal>("Rate").HasPrecision(18, 2).HasColumnType("decimal(18,2)");""");
        yield return Negative("""b.Property<decimal>("Rate").HasColumnType("decimal(19,4)");""");
        yield return Negative("""b.Property<decimal>("Rate").HasColumnType("numeric");""");
        yield return Negative("""b.Property<decimal>("Precise").HasColumnType("decimal(18,2)");""");
        yield return Negative("""b.Property<decimal>("Typed").HasColumnType("decimal(18,2)");""");
        yield return Negative("""b.Property<int>("Count").HasColumnType("int"); b.HasIndex("Count");""");
        yield return Negative("""b.Property<string>("Name").HasColumnType("nvarchar(max)"); b.HasIndex("Email");""");
        yield return Negative("""b.Property<decimal>("Total").HasColumnType("decimal(18, 2)".Replace(" ", ""));""");
    }

    [Theory]
    [Trait("Spec", "efd035-unsized-snapshot-column/Indexed nvarchar(max)")]
    [Trait("Spec", "efd035-unsized-snapshot-column/Composite index and key")]
    [Trait("Spec", "efd035-unsized-snapshot-column/Default decimal precision")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsUnsizedColumns(string source, string confidence, string message, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            Assert.StartsWith(message, diagnostic.GetMessage(), StringComparison.Ordinal);
            Assert.Equal(confidence, diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
            Assert.Equal(confidence == "high" ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info, diagnostic.Severity);
        }
    }

    [Theory]
    [Trait("Spec", "efd035-unsized-snapshot-column/Unindexed or bounded string")]
    [Trait("Spec", "efd035-unsized-snapshot-column/Configured precision")]
    [Trait("Spec", "efd035-unsized-snapshot-column/Other providers")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportSizedOrIntentionalColumns(string source)
    {
        Assert.DoesNotContain(AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd035-unsized-snapshot-column/Complete structured finding")]
    public async Task FindingsCarryTheCompleteContract()
    {
        var diagnostics = (await AnalyzeAsync(Source("""b.Property<string>("Email").HasColumnType("nvarchar(max)"); b.HasIndex("Email"); b.Property<decimal>("Rate").HasColumnType("decimal(18,2)");""")))
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, static diagnostic =>
        {
            Assert.Equal(UnsizedSnapshotColumnAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
            Assert.Equal("Correctness", diagnostic.Descriptor.Category);
            Assert.Equal("EFD035", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        });
        Assert.Equal("b.Property<string>(\"Email\").HasColumnType(\"nvarchar(max)\")", Text(diagnostics[0]));
        Assert.Equal("Model snapshot 'ShopContextModelSnapshot' maps 'Account.Email' as 'nvarchar(max)', and the same entity declares an index or key that includes 'Email'.", diagnostics[0].Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Equal(UnsizedSnapshotColumnAnalyzer.IndexedStringImpact, diagnostics[0].Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Equal(UnsizedSnapshotColumnAnalyzer.IndexedStringRemediation, diagnostics[0].Properties[DiagnosticPropertyNames.SuggestedRemediation]);
        Assert.Equal(UnsizedSnapshotColumnAnalyzer.DecimalImpact, diagnostics[1].Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("HasPrecision(precision, scale)", diagnostics[1].Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd035-unsized-snapshot-column/Suppression")]
    public async Task HonorsPragmaAndEditorConfig()
    {
        var source = Source("""
            #pragma warning disable EFD035
                    b.Property<decimal>("Rate").HasColumnType("decimal(18,2)");
            #pragma warning restore EFD035
                    b.Property<decimal>("Balance").HasColumnType("decimal(18,2)");
            """);

        Assert.Single(await AnalyzeAsync(source));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new UnsizedSnapshotColumnAnalyzer(),
            diagnosticId: UnsizedSnapshotColumnAnalyzer.DiagnosticId));
    }

    [Fact]
    [Trait("Spec", "efd035-unsized-snapshot-column/Generated snapshot")]
    public async Task ReadsGeneratedSnapshots()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync("// <auto-generated />\n" + Source("""b.Property<decimal>("Rate").HasColumnType("decimal(18,2)");""")));

        Assert.StartsWith("'Account.Rate' has no configured precision", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new UnsizedSnapshotColumnAnalyzer(),
            diagnosticId: UnsizedSnapshotColumnAnalyzer.DiagnosticId);

    private static object[] Case(string statements, string confidence, string message, int expectedCount = 1) => [Source(statements), confidence, message, expectedCount];

    private static object[] Negative(string statements) => [Source(statements)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string Source(string statements) => $$"""
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Infrastructure;

        sealed class Account
        {
            public int Id { get; set; }
            public int TenantId { get; set; }
            public string Email { get; set; } = "";
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public int Count { get; set; }
            public decimal Rate { get; set; }
            public decimal? Discount { get; set; }
            public decimal Balance { get; set; }
            public decimal Total { get; set; }

            [Precision(18, 6)]
            public decimal Precise { get; set; }

            [Column(TypeName = "decimal(18,2)")]
            public decimal Typed { get; set; }
        }

        sealed class ShopContext : DbContext
        {
        }

        [DbContext(typeof(ShopContext))]
        sealed class ShopContextModelSnapshot : ModelSnapshot
        {
            protected override void BuildModel(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity("Account", b =>
                {
                    b.Property<int>("Id");
                    b.HasKey("Id");
                    {{statements}}
                });
            }
        }
        """;
}
