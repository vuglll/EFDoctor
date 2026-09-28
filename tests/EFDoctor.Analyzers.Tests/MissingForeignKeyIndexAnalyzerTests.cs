using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class MissingForeignKeyIndexAnalyzerTests
{
    private const string Entities = """
        sealed class Principal { public int Id { get; set; } }
        class Dependent
        {
            public int Id { get; set; }
            public int PrincipalId { get; set; }
            public int TenantId { get; set; }
            public int CustomerId { get; set; }
            public int Other { get; set; }
        }
        sealed class DerivedDependent : Dependent { }
        """;

    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Positive(StringEntity("b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"));
        yield return Positive(StringEntity("b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"TenantId\", \"CustomerId\");"));
        yield return Positive(StringEntity("b.HasIndex(\"CustomerId\", \"TenantId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"TenantId\", \"CustomerId\");"));
        yield return Positive(StringEntity("b.HasIndex(\"TenantId\", \"CustomerId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"CustomerId\");"));
        yield return Positive("modelBuilder.Entity(\"Other\", b => b.HasIndex(\"PrincipalId\")); " + StringEntity("b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"));
        yield return Positive(StringEntity("b.HasIndex(\"TenantId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"TenantId\", \"CustomerId\");"));
        yield return Positive("modelBuilder.Entity(\"Base\", b => b.HasIndex(\"Other\")); modelBuilder.Entity(\"Derived\", b => { b.HasBaseType(\"Base\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\"); });");
        yield return Positive(StringEntity("b.HasIndex(\"principalId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"));
        yield return Positive(GenericEntity("b.HasOne<Principal>().WithMany().HasForeignKey(e => e.PrincipalId);"));
        yield return Positive(GenericEntity("b.HasIndex(e => new { e.CustomerId, e.TenantId }); b.HasOne<Principal>().WithMany().HasForeignKey(e => new { e.TenantId, e.CustomerId });"));
        yield return new object[]
        {
            StringEntity("b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"CustomerId\");"),
            2,
        };
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative(StringEntity("b.HasIndex(\"PrincipalId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), true);
        yield return Negative(StringEntity("b.HasIndex(\"TenantId\", \"CustomerId\", \"Other\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"TenantId\", \"CustomerId\");"), true);
        yield return Negative(StringEntity("b.HasKey(\"PrincipalId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), true);
        yield return Negative(StringEntity("b.HasAlternateKey(\"PrincipalId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), true);
        yield return Negative("modelBuilder.Entity(\"Base\", b => b.HasIndex(\"PrincipalId\")); modelBuilder.Entity(\"Derived\", b => { b.HasBaseType(\"Base\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\"); });", true);
        yield return Negative(StringEntity("var names = new[] { \"PrincipalId\" }; b.HasIndex(names); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), true);
        yield return Negative(StringEntity("var names = new[] { \"PrincipalId\" }; b.HasOne(\"Principal\", null).WithMany().HasForeignKey(names);"), true);
        yield return Negative("new FakeBuilder().HasForeignKey(\"PrincipalId\");", true, "sealed class FakeBuilder { public void HasForeignKey(params string[] names) { } }");
        yield return Negative("// b.HasForeignKey(\"PrincipalId\"); var text = \"HasForeignKey PrincipalId\";", true);
        yield return Negative("#if NEVER\nmodelBuilder.Entity(\"Dependent\", b => b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\"));\n#endif", true);
        yield return Negative(StringEntity("b.HasIndex(\"PrincipalId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), false);
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsPositiveFixtures(string body, int expectedCount)
    {
        var diagnostics = await AnalyzeSnapshotAsync(body);

        var matches = diagnostics.Where(static diagnostic => diagnostic.Id == MissingForeignKeyIndexAnalyzer.DiagnosticId).ToArray();
        Assert.True(matches.Length == expectedCount, $"Expected {expectedCount} EFD003 diagnostics but found {matches.Length}. Body:\n{body}");
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsEquivalentNpgsqlPositiveFixtures(string body, int expectedCount)
    {
        var source = AnalyzerTestHarness.SnapshotSource(body, Entities);
        var diagnostics = await AnalyzeAsync(source, includeNpgsql: true, includeRelational: true);

        Assert.Equal(expectedCount, diagnostics.Count(static diagnostic => diagnostic.Id == MissingForeignKeyIndexAnalyzer.DiagnosticId));
    }

    [Theory]
    [InlineData("b.HasIndex(\"PrincipalId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");")]
    [InlineData("b.HasKey(\"PrincipalId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");")]
    [InlineData("b.HasAlternateKey(\"PrincipalId\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");")]
    public async Task NpgsqlCoveredForeignKeysDoNotReport(string statements)
    {
        var source = AnalyzerTestHarness.SnapshotSource(StringEntity(statements), Entities);

        Assert.Empty(await AnalyzeAsync(source, includeNpgsql: true, includeRelational: true));
    }

    [Fact]
    public async Task NpgsqlBaseEntityCoverageDoesNotReport()
    {
        const string body = "modelBuilder.Entity(\"Base\", b => b.HasIndex(\"PrincipalId\")); modelBuilder.Entity(\"Derived\", b => { b.HasBaseType(\"Base\"); b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\"); });";
        var source = AnalyzerTestHarness.SnapshotSource(body, Entities);

        Assert.Empty(await AnalyzeAsync(source, includeNpgsql: true, includeRelational: true));
    }

    [Fact]
    public async Task MySqlOnlyDoesNotReport()
    {
        var source = AnalyzerTestHarness.SnapshotSource(StringEntity("b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), Entities);

        Assert.Empty(await AnalyzeAsync(source, includeMySql: true, includeRelational: true));
    }

    [Fact]
    public async Task MySqlSuppressesSqlServerInMultiProviderCompilation()
    {
        var source = AnalyzerTestHarness.SnapshotSource(StringEntity("b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), Entities);

        Assert.Empty(await AnalyzeAsync(source, includeSqlServer: true, includeMySql: true, includeRelational: true));
    }

    [Fact]
    public async Task RelationalOnlyDoesNotReport()
    {
        var source = AnalyzerTestHarness.SnapshotSource(StringEntity("b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), Entities);

        Assert.Empty(await AnalyzeAsync(source, includeRelational: true));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportNegativeFixtures(string body, bool includeSqlServer, string additionalTypes)
    {
        var source = AnalyzerTestHarness.SnapshotSource(body, Entities + additionalTypes);
        var diagnostics = await AnalyzeAsync(source, includeSqlServer, includeRelational: true);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id == MissingForeignKeyIndexAnalyzer.DiagnosticId);
    }

    [Fact]
    public void MinimalGeneratedSnapshotCompiles()
    {
        var source = AnalyzerTestHarness.SnapshotSource(StringEntity("b.HasIndex(\"PrincipalId\");"), Entities);

        var errors = AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true, includeSqlServer: true)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));
    }

    [Fact]
    public async Task OrdinaryOnModelCreatingIsNotAnalyzed()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            sealed class Principal { public int Id { get; set; } }
            sealed class Dependent { public int Id { get; set; } public int PrincipalId { get; set; } }
            sealed class Context : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Dependent>().HasOne<Principal>().WithMany().HasForeignKey(e => e.PrincipalId);
                }
            }
            """;

        Assert.Empty(await AnalyzeAsync(source, includeSqlServer: true, includeRelational: true));
    }

    [Fact]
    public async Task ReportsExactInvocationLocationsAndActionableProperties()
    {
        var source = AnalyzerTestHarness.SnapshotSource(StringEntity("b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), Entities);

        var diagnostic = Assert.Single(await AnalyzeAsync(source, includeSqlServer: true, includeRelational: true));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains("TestSnapshot", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Contains("Dependent", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Contains("PrincipalId", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Contains("SQL Server", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Contains("additional scans", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("write-heavy workloads", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]);
        Assert.Contains("outside EF migrations", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]);
        Assert.Equal("EFD003", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal(
            "b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\")",
            diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        Assert.True(diagnostic.Location.GetLineSpan().StartLinePosition.Line >= 1);
    }

    [Fact]
    public async Task EvidenceNamesNpgsqlProvider()
    {
        var source = AnalyzerTestHarness.SnapshotSource(StringEntity("b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), Entities);

        var diagnostic = Assert.Single(await AnalyzeAsync(source, includeNpgsql: true, includeRelational: true));

        Assert.Contains("PostgreSQL (Npgsql)", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
    }

    [Fact]
    public async Task PragmaSuppressionPreservesUnsuppressedNeighbor()
    {
        var body = """
            #pragma warning disable EFD003 // Intentional externally managed index.
            b.HasOne("Principal", null).WithMany().HasForeignKey("PrincipalId");
            #pragma warning restore EFD003
            b.HasOne("Principal", null).WithMany().HasForeignKey("CustomerId");
            """;

        Assert.Single(await AnalyzeSnapshotAsync(StringEntity(body)));
    }

    [Fact]
    public async Task EditorConfigurationSeverityNoneIsHonored()
    {
        var source = AnalyzerTestHarness.SnapshotSource(StringEntity("b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), Entities);

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            includeSqlServer: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new MissingForeignKeyIndexAnalyzer(),
            diagnosticId: MissingForeignKeyIndexAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SuppressMessageWithJustificationIsHonored()
    {
        var source = AnalyzerTestHarness.SnapshotSource(StringEntity("b.HasOne(\"Principal\", null).WithMany().HasForeignKey(\"PrincipalId\");"), Entities)
            .Replace(
                "sealed class TestSnapshot",
                "[SuppressMessage(\"Performance\", \"EFD003\", Justification = \"Equivalent index is managed by the DBA.\")]\nsealed class TestSnapshot",
                StringComparison.Ordinal);

        Assert.Empty(await AnalyzeAsync(source, includeSqlServer: true, includeRelational: true));
    }

    [Fact]
    public void DescriptorIsCompleteAndQualified()
    {
        Assert.Equal("EFD003", MissingForeignKeyIndexAnalyzer.Rule.Id);
        Assert.Equal("Foreign key is missing a covering index", MissingForeignKeyIndexAnalyzer.Rule.Title.ToString());
        Assert.Equal(DiagnosticSeverity.Warning, MissingForeignKeyIndexAnalyzer.Rule.DefaultSeverity);
        Assert.Contains("current EF model snapshot", MissingForeignKeyIndexAnalyzer.Rule.MessageFormat.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("always", MissingForeignKeyIndexAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("high", MissingForeignKeyIndexAnalyzer.Confidence);
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeSnapshotAsync(string body)
    {
        return AnalyzeAsync(AnalyzerTestHarness.SnapshotSource(body, Entities), includeSqlServer: true, includeRelational: true);
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        bool includeSqlServer = false,
        bool includeRelational = false,
        bool includeNpgsql = false,
        bool includeMySql = false)
    {
        return AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: includeRelational,
            includeSqlServer: includeSqlServer,
            includeNpgsql: includeNpgsql,
            includeMySql: includeMySql,
            analyzer: new MissingForeignKeyIndexAnalyzer(),
            diagnosticId: MissingForeignKeyIndexAnalyzer.DiagnosticId);
    }

    private static object[] Positive(string body) => [body, 1];

    private static object[] Negative(string body, bool includeSqlServer, string additionalTypes = "") =>
        [body, includeSqlServer, additionalTypes];

    private static string StringEntity(string statements) =>
        $$"""
        modelBuilder.Entity("Dependent", b =>
        {
        {{statements}}
        });
        """;

    private static string GenericEntity(string statements) =>
        $$"""
        modelBuilder.Entity<Dependent>(b =>
        {
        {{statements}}
        });
        """;
}
