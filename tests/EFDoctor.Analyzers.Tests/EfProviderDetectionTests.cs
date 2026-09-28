using EFDoctor.Analyzers;

namespace EFDoctor.Analyzers.Tests;

public sealed class EfProviderDetectionTests
{
    private const string EmptySource = "sealed class Subject { }";

    [Fact]
    public void ProviderMarkerMetadataNamesResolveFromReferencedPackages()
    {
        var npgsqlCompilation = Compilation(includeNpgsql: true);
        var mySqlCompilation = Compilation(includeMySql: true);

        Assert.NotNull(npgsqlCompilation.GetTypeByMetadataName(EfProviderDetection.NpgsqlMarkerMetadataName));
        Assert.NotNull(mySqlCompilation.GetTypeByMetadataName(EfProviderDetection.MySqlMarkerMetadataName));
    }

    [Theory]
    [InlineData(true, false, false, "SQL Server", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData(false, true, false, "PostgreSQL (Npgsql)", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    public void DetectsEligibleProvider(
        bool includeSqlServer,
        bool includeNpgsql,
        bool includeMySql,
        string expectedName,
        string expectedIdentity)
    {
        var result = EfProviderDetection.Detect(Compilation(includeSqlServer, includeNpgsql, includeMySql));

        Assert.False(result.HasAutoIndexingProvider);
        Assert.NotNull(result.EligibleProvider);
        Assert.Equal(expectedName, result.EligibleProvider.Name);
        Assert.Equal(expectedIdentity, result.EligibleProvider.Identity);
    }

    [Fact]
    public void MySqlSuppressesEligibleProviderWhenBothAreReferenced()
    {
        var result = EfProviderDetection.Detect(Compilation(includeSqlServer: true, includeMySql: true));

        Assert.True(result.HasAutoIndexingProvider);
        Assert.Null(result.EligibleProvider);
    }

    [Fact]
    public void MySqlOnlyIsKnownButIneligible()
    {
        var result = EfProviderDetection.Detect(Compilation(includeMySql: true));

        Assert.True(result.HasAutoIndexingProvider);
        Assert.Null(result.EligibleProvider);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoneOrRelationalOnlyIsIneligible(bool includeRelational)
    {
        var result = EfProviderDetection.Detect(AnalyzerTestHarness.CreateCompilation(
            EmptySource,
            includeEntityFramework: true,
            includeRelational: includeRelational,
            includeSqlServer: false,
            includeNpgsql: false,
            includeMySql: false,
            configuredSeverity: null,
            diagnosticId: MissingForeignKeyIndexAnalyzer.DiagnosticId));

        Assert.False(result.HasAutoIndexingProvider);
        Assert.Null(result.EligibleProvider);
    }

    [Fact]
    public void ProviderExtensionMethodsCompileInHarness()
    {
        const string npgsqlSource = """
            using Microsoft.EntityFrameworkCore;
            static class Subject
            {
                static void Configure(DbContextOptionsBuilder options) => options.UseNpgsql("Host=localhost");
            }
            """;
        const string mySqlSource = """
            using System;
            using Microsoft.EntityFrameworkCore;
            static class Subject
            {
                static void Configure(DbContextOptionsBuilder options) =>
                    options.UseMySql("Server=localhost", new MySqlServerVersion(new Version(8, 0, 36)));
            }
            """;

        AssertNoErrors(AnalyzerTestHarness.GetCompilationDiagnostics(npgsqlSource, includeNpgsql: true));
        AssertNoErrors(AnalyzerTestHarness.GetCompilationDiagnostics(mySqlSource, includeMySql: true));
    }

    private static Microsoft.CodeAnalysis.Compilation Compilation(
        bool includeSqlServer = false,
        bool includeNpgsql = false,
        bool includeMySql = false) =>
        AnalyzerTestHarness.CreateCompilation(
            EmptySource,
            includeEntityFramework: true,
            includeRelational: true,
            includeSqlServer: includeSqlServer,
            includeNpgsql: includeNpgsql,
            includeMySql: includeMySql,
            configuredSeverity: null,
            diagnosticId: MissingForeignKeyIndexAnalyzer.DiagnosticId);

    private static void AssertNoErrors(IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));
    }
}
