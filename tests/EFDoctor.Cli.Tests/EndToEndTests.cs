using System.Diagnostics;
using System.Text.Json;
using EFDoctor.Cli;

namespace EFDoctor.Cli.Tests;

public sealed class EndToEndTests
{
    [Fact]
    public async Task ConsoleScanReportsOnlyUnsuppressedMatchesInDeterministicOrder()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD001.Sample", "EFD001.Sample.csproj");

        var result = await RunCliAsync("analyze", project, "--no-color", "--quiet");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.Contains("Found 5 findings.", result.StandardOutput);
        Assert.Equal(3, CountOccurrences(result.StandardOutput, "  EFD001:"));
        Assert.Equal(2, CountOccurrences(result.StandardOutput, "  EFD002:"));
        Assert.Contains("database round trips", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intentional per-item transactions", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("process more matching rows", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AnyAsync", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Suppressed.cs", result.StandardOutput);
        Assert.DoesNotContain('\u001b', result.StandardOutput);
    }

    [Fact]
    public async Task JsonScanProducesSchemaVersionOneAndCompleteOrderedFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD001.Sample", "EFD001.Sample.csproj");

        var result = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(5, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(5, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Contains(finding.GetProperty("ruleId").GetString(), new[] { "EFD001", "EFD002" });
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
            Assert.True(finding.GetProperty("range").GetProperty("startLine").GetInt32() > 0);
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("evidence").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("likelyImpact").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("suggestedRemediation").GetString()));
        });
        Assert.Equal(3, findings.Count(static finding => finding.GetProperty("ruleId").GetString() == "EFD001"));
        Assert.Equal(2, findings.Count(static finding => finding.GetProperty("ruleId").GetString() == "EFD002"));
        Assert.All(findings.Where(static finding => finding.GetProperty("ruleId").GetString() == "EFD002"), static finding =>
        {
            Assert.Equal("EFD002", finding.GetProperty("documentationReference").GetString());
            Assert.Contains("Count", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("Any", finding.GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
        });
        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
        Assert.DoesNotContain('\u001b', result.StandardOutput);
    }

    [Fact]
    public async Task Efd003FixtureProducesActionableConsoleAndJsonResults()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD003.Sample", "EFD003.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 1 finding.", console.StandardOutput);
        Assert.Contains("EFD003: Foreign key is missing a covering index", console.StandardOutput);
        Assert.Contains("TenantId, ParentId", console.StandardOutput);
        Assert.Contains("additional scans", console.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outside EF migrations", console.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PragmaSuppressed", console.StandardOutput);
        Assert.DoesNotContain("AttributeSuppressed", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var finding = Assert.Single(document.RootElement.GetProperty("findings").EnumerateArray().ToArray());
        Assert.Equal("EFD003", finding.GetProperty("ruleId").GetString());
        Assert.Equal("Foreign key is missing a covering index", finding.GetProperty("ruleTitle").GetString());
        Assert.Equal("warning", finding.GetProperty("severity").GetString());
        Assert.Equal("high", finding.GetProperty("confidence").GetString());
        Assert.EndsWith("tests/Fixtures/EFD003.Sample/SampleSnapshots.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
        Assert.Equal(26, finding.GetProperty("range").GetProperty("startLine").GetInt32());
        Assert.Equal(13, finding.GetProperty("range").GetProperty("startColumn").GetInt32());
        Assert.Equal(28, finding.GetProperty("range").GetProperty("endLine").GetInt32());
        Assert.Equal(55, finding.GetProperty("range").GetProperty("endColumn").GetInt32());
        Assert.Contains("CurrentModelSnapshot", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("UncoveredDependent", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Equal("EFD003", finding.GetProperty("documentationReference").GetString());
    }

    [Fact]
    public async Task Efd004FixtureProducesOnlyActionableUnsuppressedConsoleAndJsonResults()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD004.Sample", "EFD004.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 2 findings.", console.StandardOutput);
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "  EFD004:"));
        Assert.Contains("Query is materialized before SQL-capable composition", console.StandardOutput);
        Assert.Contains("buffer", console.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("small bounded results", console.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Suppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD004", finding.GetProperty("ruleId").GetString());
            Assert.Equal("Query is materialized before SQL-capable composition", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD004.Sample/MaterializationCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("DbSet origin", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD004", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal(21, findings[0].GetProperty("range").GetProperty("startLine").GetInt32());
        Assert.Equal(16, findings[0].GetProperty("range").GetProperty("startColumn").GetInt32());
        Assert.Equal(21, findings[0].GetProperty("range").GetProperty("endLine").GetInt32());
        Assert.Equal(42, findings[0].GetProperty("range").GetProperty("endColumn").GetInt32());
        Assert.Equal(26, findings[1].GetProperty("range").GetProperty("startLine").GetInt32());
        Assert.Equal(23, findings[1].GetProperty("range").GetProperty("startColumn").GetInt32());
        Assert.Equal(26, findings[1].GetProperty("range").GetProperty("endLine").GetInt32());
        Assert.Equal(55, findings[1].GetProperty("range").GetProperty("endColumn").GetInt32());
        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    [Trait("Spec", "efd005-unbounded-query-materialization/CLI end-to-end fixture")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Console and JSON reporting")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Full regression verification")]
    public async Task Efd005FixtureProducesBoundAwareNonDuplicateConsoleAndJsonResults()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD005.Sample", "EFD005.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 6 findings.", console.StandardOutput);
        Assert.Equal(5, CountOccurrences(console.StandardOutput, "  EFD005:"));
        Assert.Equal(1, CountOccurrences(console.StandardOutput, "  EFD004:"));
        Assert.Contains("Query materialization has no recognized row bound", console.StandardOutput);
        Assert.Contains("Confidence: High", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Confidence: Medium", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Confidence: Advisory", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Severity: Info", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("hydrating children for a known set of parents", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Do not add an arbitrary limit", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(6, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(6, findings.Length);
        var efd005 = findings.Where(static finding => finding.GetProperty("ruleId").GetString() == "EFD005").ToArray();
        Assert.Equal(5, efd005.Length);
        Assert.All(efd005, static finding =>
        {
            Assert.Equal("Query materialization has no recognized row bound", finding.GetProperty("ruleTitle").GetString());
            Assert.EndsWith("tests/Fixtures/EFD005.Sample/MaterializationCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("recognized row bound", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD005", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal(1, efd005.Count(static finding => finding.GetProperty("confidence").GetString() == "high"
            && finding.GetProperty("severity").GetString() == "warning"));
        Assert.Equal(2, efd005.Count(static finding => finding.GetProperty("confidence").GetString() == "medium"
            && finding.GetProperty("severity").GetString() == "warning"));
        Assert.Equal(2, efd005.Count(static finding => finding.GetProperty("confidence").GetString() == "advisory"
            && finding.GetProperty("severity").GetString() == "info"));
        Assert.Contains(efd005, static finding => finding.GetProperty("evidence").GetString()!.Contains("KeyEqualityByName (Medium)", StringComparison.Ordinal));
        Assert.Contains(efd005, static finding => finding.GetProperty("evidence").GetString()!.Contains("TimeWindow (Weak)", StringComparison.Ordinal));
        Assert.Equal((28, 16, 28, 42), Range(efd005[0]));
        Assert.Equal((33, 16, 33, 82), Range(efd005[1]));

        var overlap = Assert.Single(findings, static finding => finding.GetProperty("range").GetProperty("startLine").GetInt32() == 55);
        Assert.Equal("EFD004", overlap.GetProperty("ruleId").GetString());
        Assert.DoesNotContain(findings, static finding =>
            finding.GetProperty("ruleId").GetString() == "EFD005"
            && finding.GetProperty("range").GetProperty("startLine").GetInt32() == 55);

        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    [Trait("Spec", "cli-ef-resolution-check/Resolved EF project produces no warning")]
    public async Task TestProjectIsSkippedByDefault()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD005.TestSample", "EFD005.TestSample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");

        Assert.Equal(0, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.DoesNotContain("Severity:", console.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestProjectFindingsFromMultipleRulesAreDowngradedButRemainReported()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD005.TestSample", "EFD005.TestSample.csproj");

        var console = await RunCliAsync("analyze", project, "--include-test-projects", "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--include-test-projects", "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 14 findings.", console.StandardOutput);
        Assert.Contains("EFD001", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD005", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD012", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD013", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD017", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD018", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD019", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD025", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD009", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD023", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD029", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD027", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD037", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("EFD038", console.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(14, CountOccurrences(console.StandardOutput, "Severity: Info"));
        Assert.Equal(14, CountOccurrences(console.StandardOutput, "Confidence: Advisory"));

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(14, findings.Length);
        Assert.Equal(new[] { "EFD005", "EFD001", "EFD012", "EFD013", "EFD017", "EFD018", "EFD019", "EFD025", "EFD009", "EFD023", "EFD029", "EFD027", "EFD037", "EFD038" }, findings.Select(static finding => finding.GetProperty("ruleId").GetString()));
        Assert.All(findings, static finding =>
        {
            Assert.Equal("info", finding.GetProperty("severity").GetString());
            Assert.Equal("advisory", finding.GetProperty("confidence").GetString());
        });
    }

    [Fact]
    [Trait("Spec", "efd006-multiple-collection-include/Console and JSON reporting")]
    [Trait("Spec", "efd006-multiple-collection-include/CLI end-to-end fixture")]
    [Trait("Spec", "efd006-multiple-collection-include/Full regression verification")]
    public async Task Efd006FixtureProducesOnlySingleQuerySiblingCollectionFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD006.Sample", "EFD006.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 2 findings.", console.StandardOutput);
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "  EFD006:"));
        Assert.Contains("Multiple sibling collection includes may create a cartesian explosion", console.StandardOutput);
        Assert.Contains("medium", console.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Blog.Posts, Blog.Contributors", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("AsSplitQuery", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD006", finding.GetProperty("ruleId").GetString());
            Assert.Equal("Multiple sibling collection includes may create a cartesian explosion", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal("medium", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD006.Sample/IncludeCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("Blog.Posts, Blog.Contributors", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD006", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((25, 9, 25, 85), Range(findings[0]));
        Assert.Equal((28, 9, 28, 85), Range(findings[1]));
        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    public async Task Efd011FixtureProducesOnlyDirectUnsuppressedBlockingFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD011.Sample", "EFD011.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 3 findings.", console.StandardOutput);
        Assert.Equal(3, CountOccurrences(console.StandardOutput, "  EFD011:"));
        Assert.Contains("Synchronous blocking on an EF Core async operation", console.StandardOutput);
        Assert.Contains("high", console.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thread-pool starvation", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("propagate async", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(3, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD011", finding.GetProperty("ruleId").GetString());
            Assert.Equal("Synchronous blocking on an EF Core async operation", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD011.Sample/BlockingCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("resolved EF Core operation", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD011", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((18, 9, 18, 43), Range(findings[0]));
        Assert.Equal((21, 9, 21, 42), Range(findings[1]));
        Assert.Equal((24, 9, 24, 63), Range(findings[2]));
        Assert.Contains(".Result", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains(".Wait()", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("GetAwaiter", findings[2].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    public async Task Efd012FixtureProducesOnlyProvenUnsafeRawSqlFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD012.Sample", "EFD012.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 2 findings.", console.StandardOutput);
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "  EFD012:"));
        Assert.Contains("Dynamic SQL passed to an EF Core raw-SQL API", console.StandardOutput);
        Assert.Contains("SQL injection", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("allow-list", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD012", finding.GetProperty("ruleId").GetString());
            Assert.Equal("Dynamic SQL passed to an EF Core raw-SQL API", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD012.Sample/RawSqlCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("Resolved EF Core raw-SQL operation", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD012", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((18, 37, 18, 78), Range(findings[0]));
        Assert.Equal((23, 47, 23, 50), Range(findings[1]));
        Assert.Contains("interpolation", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("local variable", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    public async Task Efd013FixtureProducesOnlyConservativeBulkOperationCandidates()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD013.Sample", "EFD013.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 2 findings.", console.StandardOutput);
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "  EFD013:"));
        Assert.Contains("Load-loop-save pattern may support a bulk operation", console.StandardOutput);
        Assert.Contains("Confidence: Medium", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("ExecuteUpdate", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("ExecuteDeleteAsync", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("tracked-entity state", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD013", finding.GetProperty("ruleId").GetString());
            Assert.Equal("Load-loop-save pattern may support a bulk operation", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal("medium", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD013.Sample/BulkOperationCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("matching save", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("per-row", finding.GetProperty("likelyImpact").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD013", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((21, 20, 21, 86), Range(findings[0]));
        Assert.Equal((33, 26, 33, 97), Range(findings[1]));
        Assert.Contains("uniform assignments", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("ExecuteUpdate", findings[0].GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
        Assert.Contains("delete", findings[1].GetProperty("evidence").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ExecuteDeleteAsync", findings[1].GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    public async Task Efd017FixtureProducesOnlyUnsuppressedNonEntityProjectionFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD017.Sample", "EFD017.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 2 findings.", console.StandardOutput);
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "  EFD017:"));
        Assert.Contains("Include is ignored by a later non-entity projection", console.StandardOutput);
        Assert.Contains("Confidence: High", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Order.Lines", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("remove the redundant Include", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD017", finding.GetProperty("ruleId").GetString());
            Assert.Equal("Include is ignored by a later non-entity projection", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD017.Sample/ProjectionCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("DbSet origin 'context.Orders'", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("does not populate navigation state", finding.GetProperty("likelyImpact").GetString(), StringComparison.Ordinal);
            Assert.Contains("return the entity", finding.GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD017", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((31, 9, 31, 79), Range(findings[0]));
        Assert.Equal((34, 9, 34, 153), Range(findings[1]));
        Assert.Contains("Order.Lines", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("a scalar value of type 'int'", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("Order.Customer", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("'OrderSummary'", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("already reads data through the included navigation", findings[1].GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    public async Task Efd018FixtureProducesOnlyUnsuppressedDiscardedTaskFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD018.Sample", "EFD018.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 3 findings.", console.StandardOutput);
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "  EFD018:"));
        Assert.Equal(1, CountOccurrences(console.StandardOutput, "  EFD011:"));
        Assert.Contains("Unawaited EF Core async operation", console.StandardOutput);
        Assert.Contains("Confidence: High", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("separately scoped context", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var allFindings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(new[] { "EFD018", "EFD018", "EFD011" }, allFindings.Select(static finding => finding.GetProperty("ruleId").GetString()));
        var findings = allFindings.Where(static finding => finding.GetProperty("ruleId").GetString() == "EFD018").ToArray();
        Assert.All(findings, static finding =>
        {
            Assert.Equal("Unawaited EF Core async operation", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD018.Sample/DiscardCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("resolved EF Core operation", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("exceptions are never observed", finding.GetProperty("likelyImpact").GetString(), StringComparison.Ordinal);
            Assert.Contains("Await the EF Core operation", finding.GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD018", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((21, 9, 21, 35), Range(findings[0]));
        Assert.Equal((26, 9, 26, 80), Range(findings[1]));
        Assert.Contains("SaveChangesAsync", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("as a standalone statement", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("ExecuteDeleteAsync", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("explicit discard assignment", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Equal((35, 9, 35, 42), Range(allFindings[2]));
        var orderedKeys = allFindings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    public async Task Efd019FixtureProducesReductionFindingsWithoutEfd005Duplicates()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD019.Sample", "EFD019.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 8 findings.", console.StandardOutput);
        Assert.Equal(6, CountOccurrences(console.StandardOutput, "  EFD019:"));
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "  EFD005:"));
        Assert.Contains("Query is materialized and then immediately reduced", console.StandardOutput);
        Assert.Contains("Confidence: High", console.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("CountAsync", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(8, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var allFindings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(new[] { "EFD019", "EFD019", "EFD019", "EFD005", "EFD005", "EFD019", "EFD019", "EFD019" }, allFindings.Select(static finding => finding.GetProperty("ruleId").GetString()));
        var findings = allFindings.Where(static finding => finding.GetProperty("ruleId").GetString() == "EFD019").ToArray();
        Assert.All(findings, static finding =>
        {
            Assert.Equal("Query is materialized and then immediately reduced", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD019.Sample/ReductionCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("DbSet origin 'context.Invoices'", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("buffers every row", finding.GetProperty("likelyImpact").GetString(), StringComparison.Ordinal);
            Assert.Contains("Keep the materialized list only when its rows are also needed elsewhere", finding.GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD019", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((20, 9, 20, 34), Range(findings[0]));
        Assert.Equal((23, 16, 23, 78), Range(findings[1]));
        Assert.Equal((26, 9, 26, 35), Range(findings[2]));
        Assert.Contains("'Enumerable.FirstOrDefault'", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("'List<T>.Count' property", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("'CountAsync'", findings[1].GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
        Assert.Contains("'Enumerable.Sum'", findings[2].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Equal((57, 9, 57, 65), Range(findings[3]));
        Assert.StartsWith("Apply 'Any' to the query", findings[3].GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
        Assert.Contains("The count is compared as 'count > 0', which only tests existence.", findings[3].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Equal((62, 22, 62, 79), Range(findings[4]));
        Assert.Equal("This EF Core query is fully materialized and then used only for its count through local 'unpaid'", findings[4].GetProperty("message").GetString());
        Assert.StartsWith("Apply 'Any' to the query", findings[4].GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
        Assert.Contains("Negate it where the code tests for an empty result.", findings[4].GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
        Assert.Contains("uses only for its count: 'unpaid.Count == 0'.", findings[4].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Equal((69, 30, 69, 60), Range(findings[5]));
        Assert.Contains("'CountAsync'", findings[5].GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
        Assert.Equal((33, 24, 33, 49), Range(allFindings[3]));
        Assert.Equal((41, 9, 41, 34), Range(allFindings[4]));
        var orderedKeys = allFindings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    public async Task Efd025FixtureProducesOnlyUnsuppressedRedundantIncludeFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD025.Sample", "EFD025.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 3 findings.", console.StandardOutput);
        Assert.Equal(3, CountOccurrences(console.StandardOutput, "  EFD025:"));
        Assert.Contains("Include path is redundant with another Include in the same query", console.StandardOutput);
        Assert.Equal(3, CountOccurrences(console.StandardOutput, "Severity: Info | Confidence: High"));
        Assert.Contains("Delete the redundant Include/ThenInclude chain", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(3, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD025", finding.GetProperty("ruleId").GetString());
            Assert.Equal("Include path is redundant with another Include in the same query", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("info", finding.GetProperty("severity").GetString());
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD025.Sample/IncludeCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("DbSet origin 'context.Orders'", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("changes neither the generated SQL nor the loaded data", finding.GetProperty("likelyImpact").GetString(), StringComparison.Ordinal);
            Assert.Contains("Delete the redundant Include/ThenInclude chain", finding.GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD025", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((40, 57, 40, 89), Range(findings[0]));
        Assert.Equal((43, 24, 43, 53), Range(findings[1]));
        Assert.Equal((46, 52, 46, 79), Range(findings[2]));
        Assert.Contains("'Order.Customer' is a duplicate", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("'Order.Lines' is covered by a longer path: 'Order.Lines.Product'", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("\"Customer.Address\" is a duplicate", findings[2].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Complete structured finding")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Console and JSON reporting")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/CLI end-to-end fixture")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Pragma suppression")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/Editor configuration suppression")]
    [Trait("Spec", "efd029-orderby-replaces-ordering/SuppressMessage attribute")]
    public async Task Efd029FixtureProducesOnlyUnsuppressedReplacedOrderingFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD029.Sample", "EFD029.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 2 findings.", console.StandardOutput);
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "  EFD029:"));
        Assert.Contains("OrderBy discards an earlier ordering", console.StandardOutput);
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "Severity: Warning | Confidence: High"));
        Assert.Contains("replace this OrderBy with ThenBy", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD029", finding.GetProperty("ruleId").GetString());
            Assert.Equal("OrderBy discards an earlier ordering", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD029.Sample/OrderingCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("DbSet origin 'context.Orders'", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("EF Core translates only the last OrderBy", finding.GetProperty("likelyImpact").GetString(), StringComparison.Ordinal);
            Assert.Contains("replace this OrderBy with ThenBy", finding.GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD029", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((21, 57, 21, 91), Range(findings[0]));
        Assert.Equal((25, 127, 25, 171), Range(findings[1]));
        Assert.Contains("already ordered by 'OrderBy', which this OrderBy replaces", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("already ordered by 'OrderBy -> ThenBy', which this OrderByDescending replaces", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    [Trait("Spec", "efd038-stale-tracked-entities/Complete structured finding")]
    [Trait("Spec", "efd038-stale-tracked-entities/Diagnostic location")]
    [Trait("Spec", "efd038-stale-tracked-entities/Standard suppression")]
    [Trait("Spec", "efd038-stale-tracked-entities/Same type loaded again")]
    [Trait("Spec", "efd038-stale-tracked-entities/Left stale")]
    public async Task Efd038FixtureProducesOnlyUnsuppressedStaleEntityFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD038.Sample", "EFD038.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 2 findings.", console.StandardOutput);
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "  EFD038:"));
        Assert.Contains("Bulk operation leaves tracked entities stale", console.StandardOutput);
        Assert.Equal(1, CountOccurrences(console.StandardOutput, "Severity: Warning | Confidence: High"));
        Assert.Equal(1, CountOccurrences(console.StandardOutput, "Severity: Warning | Confidence: Medium"));
        Assert.Contains("ChangeTracker.Clear()", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD038", finding.GetProperty("ruleId").GetString());
            Assert.Equal("Bulk operation leaves tracked entities stale", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.EndsWith("tests/Fixtures/EFD038.Sample/StaleEntityCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("on context 'context'", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("bypass the change tracker", finding.GetProperty("likelyImpact").GetString(), StringComparison.Ordinal);
            Assert.Contains("ChangeTracker.Clear()", finding.GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD038", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((25, 15, 25, 161), Range(findings[0]));
        Assert.Equal("high", findings[0].GetProperty("confidence").GetString());
        Assert.Equal("This ExecuteUpdateAsync bypasses the change tracker, so the 'Product' entities already tracked through 'product' are stale", findings[0].GetProperty("message").GetString());
        Assert.Contains("The method then loads 'Product' again with 'context.Products.FindAsync(id)' on line 26", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Equal((35, 9, 35, 84), Range(findings[1]));
        Assert.Equal("medium", findings[1].GetProperty("confidence").GetString());
        Assert.Contains("Nothing later in this method uses 'products'", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd037-entity-over-fetch/Complete structured finding")]
    [Trait("Spec", "efd037-entity-over-fetch/Diagnostic location")]
    [Trait("Spec", "efd037-entity-over-fetch/Standard suppression")]
    public async Task Efd037FixtureProducesOnlyUnsuppressedOverFetchFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD037.Sample", "EFD037.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 2 findings.", console.StandardOutput);
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "  EFD037:"));
        Assert.Equal(2, CountOccurrences(console.StandardOutput, "Severity: Info | Confidence: Advisory"));
        Assert.Contains("Entities are materialized but only a few columns are read", console.StandardOutput);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray()
            .Where(static finding => finding.GetProperty("ruleId").GetString() == "EFD037")
            .ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("Entities are materialized but only a few columns are read", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("info", finding.GetProperty("severity").GetString());
            Assert.Equal("advisory", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD037.Sample/OverFetchCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("DbSet origin 'context.Invoices'", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("materialized into local 'invoices'", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("actual cost depends", finding.GetProperty("likelyImpact").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD037", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((37, 30, 37, 111), Range(findings[0]));
        Assert.Equal((44, 24, 44, 105), Range(findings[1]));
        Assert.Equal("This query loads complete 'Invoice' entities, but the method reads only 2 of their 8 scalar properties", findings[0].GetProperty("message").GetString());
        Assert.Contains("reads 2 of the 8 scalar properties of 'Invoice': Id, Total.", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("Select(x => new { x.Id, x.Total })", findings[0].GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
        Assert.Contains("reads 3 of the 8 scalar properties of 'Invoice': Number, PaidUtc, Total.", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Complete structured finding")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Console and JSON reporting")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/CLI end-to-end fixture")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Pragma suppression")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/Editor configuration suppression")]
    [Trait("Spec", "efd027-concurrent-dbcontext-operations/SuppressMessage attribute")]
    public async Task Efd027FixtureProducesOnlyUnsuppressedConcurrentOperationFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD027.Sample", "EFD027.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 3 findings.", console.StandardOutput);
        Assert.Equal(3, CountOccurrences(console.StandardOutput, "  EFD027:"));
        Assert.Contains("Concurrent operations on one DbContext", console.StandardOutput);
        Assert.Equal(3, CountOccurrences(console.StandardOutput, "Severity: Warning | Confidence: High"));
        Assert.Contains("Await each operation before starting the next one", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(3, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD027", finding.GetProperty("ruleId").GetString());
            Assert.Equal("Concurrent operations on one DbContext", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD027.Sample/ConcurrencyCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("context 'context'", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("A DbContext is not thread-safe", finding.GetProperty("likelyImpact").GetString(), StringComparison.Ordinal);
            Assert.Contains("IDbContextFactory<TContext>", finding.GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD027", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((26, 51, 26, 79), Range(findings[0]));
        Assert.Equal((32, 31, 32, 61), Range(findings[1]));
        Assert.Equal((38, 51, 38, 79), Range(findings[2]));
        Assert.Contains("is passed to Task.WhenAll after 'context.Orders.CountAsync()'", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("starts while task local 'orders'", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("captured from outside the selector", findings[2].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    public async Task Efd009FixtureProducesCaseTransformFindingsAndHandsContainsToEfd023()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD009.Sample", "EFD009.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 4 findings.", console.StandardOutput);
        Assert.Equal(3, CountOccurrences(console.StandardOutput, "  EFD009:"));
        Assert.Contains("Case transformation on a column prevents an index seek", console.StandardOutput);
        Assert.Equal(3, CountOccurrences(console.StandardOutput, "Severity: Warning | Confidence: Medium"));
        Assert.Contains("SQL Server's default collations are case-insensitive", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(4, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var allFindings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(4, allFindings.Length);

        // A case-transformed column searched with Contains is EFD023's finding, not EFD009's.
        var handedOff = allFindings[3];
        Assert.Equal("EFD023", handedOff.GetProperty("ruleId").GetString());
        Assert.Equal((31, 45, 31, 84), Range(handedOff));

        var findings = allFindings.Take(3).ToArray();
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD009", finding.GetProperty("ruleId").GetString());
            Assert.Equal("Case transformation on a column prevents an index seek", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal("medium", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD009.Sample/LookupCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("DbSet origin 'context.Customers'", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("column 'Customer.Email'", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.EndsWith("Referenced EF Core provider: SQL Server.", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("prevents the database from using an ordinary index", finding.GetProperty("likelyImpact").GetString(), StringComparison.Ordinal);
            Assert.StartsWith("SQL Server's default collations are case-insensitive", finding.GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD009", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((19, 45, 19, 69), Range(findings[0]));
        Assert.Equal((22, 45, 22, 69), Range(findings[1]));
        Assert.Equal((25, 54, 25, 78), Range(findings[2]));
        Assert.Contains("applies ToLower() to column 'Customer.Email' and compares the result using ==", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("applies ToUpper() to column 'Customer.Email' and compares the result using StartsWith", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("applies ToLower() to column 'Customer.Email' and compares the result using ==", findings[2].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        var orderedKeys = allFindings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/Finding contract")]
    [Trait("Spec", "efd023-leading-wildcard-search/SQL Server provider referenced")]
    [Trait("Spec", "efd023-leading-wildcard-search/Suppressed finding")]
    public async Task Efd023FixtureProducesOnlyUnsuppressedLeadingWildcardFindings()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD023.Sample", "EFD023.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 4 findings.", console.StandardOutput);
        Assert.Equal(4, CountOccurrences(console.StandardOutput, "  EFD023:"));
        Assert.Contains("Leading-wildcard search on a column cannot seek an index", console.StandardOutput);
        Assert.Equal(4, CountOccurrences(console.StandardOutput, "Severity: Info | Confidence: Advisory"));
        Assert.Contains("use a full-text index with EF.Functions.Contains or EF.Functions.FreeText", console.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorConfigSuppressed.cs", console.StandardOutput);
        Assert.DoesNotContain('\u001b', console.StandardOutput);

        Assert.Equal(1, json.ExitCode);
        Assert.Empty(json.StandardError);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(4, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(4, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD023", finding.GetProperty("ruleId").GetString());
            Assert.Equal("Leading-wildcard search on a column cannot seek an index", finding.GetProperty("ruleTitle").GetString());
            Assert.Equal("info", finding.GetProperty("severity").GetString());
            Assert.Equal("advisory", finding.GetProperty("confidence").GetString());
            Assert.EndsWith("tests/Fixtures/EFD023.Sample/SearchCases.cs", finding.GetProperty("sourceFile").GetString(), StringComparison.Ordinal);
            Assert.Contains("DbSet origin 'context.Customers'", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.EndsWith("Referenced EF Core provider: SQL Server.", finding.GetProperty("evidence").GetString(), StringComparison.Ordinal);
            Assert.Contains("cannot seek an ordinary index", finding.GetProperty("likelyImpact").GetString(), StringComparison.Ordinal);
            Assert.StartsWith("For word or phrase search on SQL Server", finding.GetProperty("suggestedRemediation").GetString(), StringComparison.Ordinal);
            Assert.Equal("EFD023", finding.GetProperty("documentationReference").GetString());
        });
        Assert.Equal((20, 45, 20, 73), Range(findings[0]));
        Assert.Equal((23, 45, 23, 76), Range(findings[1]));
        Assert.Equal((26, 45, 26, 83), Range(findings[2]));
        Assert.Equal((29, 45, 29, 89), Range(findings[3]));
        Assert.Contains("searches column 'Customer.Name' with Contains", findings[0].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("searches column 'Customer.Email' with EndsWith", findings[1].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("searches column 'Customer.Name' after ToLower() with Contains", findings[2].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        Assert.Contains("the LIKE pattern starts with '%'", findings[3].GetProperty("evidence").GetString(), StringComparison.Ordinal);
        var orderedKeys = findings.Select(static finding => $"{finding.GetProperty("sourceFile").GetString()}:{finding.GetProperty("range").GetProperty("startLine").GetInt32():D6}:{finding.GetProperty("range").GetProperty("startColumn").GetInt32():D6}:{finding.GetProperty("ruleId").GetString()}").ToArray();
        Assert.Equal(orderedKeys.OrderBy(static key => key, StringComparer.Ordinal), orderedKeys);
    }

    [Fact]
    public async Task CleanAndInvalidTargetsUseStableExitCodes()
    {
        var cleanProject = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD001.Clean", "EFD001.Clean.csproj");

        var clean = await RunCliAsync("analyze", cleanProject, "--format", "json");
        var invalid = await RunCliAsync("analyze", Path.Combine(RepositoryRoot(), "missing.csproj"));

        Assert.Equal(0, clean.ExitCode);
        using var cleanDocument = JsonDocument.Parse(clean.StandardOutput);
        Assert.Equal(0, cleanDocument.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        Assert.Equal(2, invalid.ExitCode);
        Assert.Contains("does not exist", invalid.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkingDirectoryGlobalJsonDoesNotAffectAnalysisOfAnotherTarget()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"efdoctor-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "global.json"),
            """{ "sdk": { "version": "99.0.100", "rollForward": "disable" } }""");
        var cleanProject = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD001.Clean", "EFD001.Clean.csproj");

        try
        {
            var result = await RunCliInAsync(temporaryDirectory, "analyze", cleanProject, "--format", "json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal(0, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task TargetPinningMissingSdkKeepsJsonStandardOutputPure()
    {
        // hostfxr prints the installed-SDK list to the process's own standard output when it can't
        // satisfy a global.json, so only an out-of-process run can see whether stdout stays pure.
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"efdoctor-nosdk-{Guid.NewGuid():N}");
        var project = await WriteUnrestoredProjectAsync(temporaryDirectory, "Pinned", usesEfCore: false);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "global.json"),
            """{ "sdk": { "version": "99.0.100", "rollForward": "disable" } }""");

        try
        {
            var result = await RunCliAsync("analyze", project, "--format", "json");

            Assert.Equal(2, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var error = document.RootElement.GetProperty("error");
            Assert.Equal("analysis-failure", error.GetProperty("code").GetString());
            Assert.Contains("No installed .NET SDK satisfies the global.json", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Contains("99.0.100", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.DoesNotMatch(@"(?m)^\d+\.\d+\.\d+\S* \[", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingButBrokenProjectReturnsAnalysisFailure()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"efdoctor-broken-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var project = Path.Combine(temporaryDirectory, "Broken.csproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"missing.targets\" /></Project>");

        try
        {
            var result = await RunCliAsync("analyze", project, "--no-color");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("MSBuild could not load", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SolutionWithOneUnloadableProjectStillAnalyzesTheRest()
    {
        var root = RepositoryRoot();
        var goodProject = Path.Combine(root, "tests", "Fixtures", "EFD001.Sample", "EFD001.Sample.csproj");

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"efdoctor-partial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "Broken"));
        var brokenProject = Path.Combine(temporaryDirectory, "Broken", "Broken.csproj");
        await File.WriteAllTextAsync(brokenProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"missing.targets\" /></Project>");

        var solution = Path.Combine(temporaryDirectory, "Partial.sln");
        await File.WriteAllTextAsync(solution, BuildSolutionFile(goodProject, brokenProject));

        try
        {
            var result = await RunCliAsync("analyze", solution, "--no-color");

            // The broken sibling emits a workspace load failure, but the good project
            // still produces a compilation, so the scan reports its findings (exit 1)
            // rather than aborting with the analysis-failure exit code.
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("EFD001", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("MSBuild could not load", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MixedSolutionSkipsTestProjectByDefaultAndIncludesItWithFlag()
    {
        var root = RepositoryRoot();
        var productionProject = Path.Combine(root, "tests", "Fixtures", "EFD001.Sample", "EFD001.Sample.csproj");
        var testProject = Path.Combine(root, "tests", "Fixtures", "EFD005.TestSample", "EFD005.TestSample.csproj");

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"efdoctor-mixed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var solution = Path.Combine(temporaryDirectory, "Mixed.sln");
        await File.WriteAllTextAsync(solution, BuildSolutionFile(productionProject, testProject));

        try
        {
            var byDefault = await RunCliAsync("analyze", solution, "--no-color", "--quiet");
            var included = await RunCliAsync("analyze", solution, "--include-test-projects", "--no-color", "--quiet");

            // Default: production findings are reported, the test project is skipped
            // (no advisory/Info findings from it).
            Assert.Equal(1, byDefault.ExitCode);
            Assert.Contains("EFD001", byDefault.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Confidence: Advisory", byDefault.StandardOutput, StringComparison.Ordinal);

            // Included: production findings plus the test project's downgraded findings.
            Assert.Equal(1, included.ExitCode);
            Assert.Contains("EFD001", included.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Confidence: Advisory", included.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("Spec", "cli-ef-resolution-check/Unrestored EF-only project")]
    [Trait("Spec", "cli-ef-resolution-check/Unrestored EF-only project in JSON mode")]
    public async Task UnrestoredEfProjectFailsInsteadOfReportingClean()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"efdoctor-unrestored-{Guid.NewGuid():N}");
        var project = await WriteUnrestoredProjectAsync(temporaryDirectory, "Unrestored", usesEfCore: true);

        try
        {
            var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
            var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

            Assert.Equal(2, console.ExitCode);
            Assert.DoesNotContain("No findings", console.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("'Unrestored'", console.StandardError, StringComparison.Ordinal);
            Assert.Contains("EF Core types could not be resolved", console.StandardError, StringComparison.Ordinal);
            Assert.Contains("Restore and build the project", console.StandardError, StringComparison.Ordinal);

            Assert.Equal(2, json.ExitCode);
            using var document = JsonDocument.Parse(json.StandardOutput);
            Assert.Equal("analysis-failure", document.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains("'Unrestored'", document.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("Spec", "cli-ef-resolution-check/Unanalyzable EF project alongside analyzable ones")]
    [Trait("Spec", "cli-ef-resolution-check/JSON output stays a pure report")]
    public async Task SolutionWithUnrestoredEfProjectWarnsAndAnalyzesTheRest()
    {
        var goodProject = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD001.Sample", "EFD001.Sample.csproj");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"efdoctor-partial-ef-{Guid.NewGuid():N}");
        var unrestored = await WriteUnrestoredProjectAsync(temporaryDirectory, "Unrestored", usesEfCore: true);
        var solution = Path.Combine(temporaryDirectory, "Partial.sln");
        await File.WriteAllTextAsync(solution, BuildSolutionFile(goodProject, unrestored));

        try
        {
            var console = await RunCliAsync("analyze", solution, "--no-color", "--quiet");
            var json = await RunCliAsync("analyze", solution, "--format", "json", "--quiet");

            Assert.Equal(1, console.ExitCode);
            Assert.Contains("EFD001", console.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("EFDoctor warning:", console.StandardError, StringComparison.Ordinal);
            Assert.Contains("'Unrestored'", console.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("EFDoctor warning", console.StandardOutput, StringComparison.Ordinal);

            Assert.Equal(1, json.ExitCode);
            using var document = JsonDocument.Parse(json.StandardOutput);
            Assert.Contains(document.RootElement.GetProperty("findings").EnumerateArray(), static finding => finding.GetProperty("ruleId").GetString() == "EFD001");
            Assert.Contains("'Unrestored'", json.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("Spec", "cli-ef-resolution-check/Project without EF Core usage is not flagged")]
    public async Task UnrestoredProjectWithoutEfCoreIsNotFlagged()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"efdoctor-plain-{Guid.NewGuid():N}");
        var project = await WriteUnrestoredProjectAsync(temporaryDirectory, "Plain", usesEfCore: false);

        try
        {
            var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");

            Assert.Equal(0, console.ExitCode);
            Assert.DoesNotContain("EFDoctor warning", console.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("EF Core types could not be resolved", console.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("Spec", "cli-ef-resolution-check/Skipped test project is not checked")]
    public async Task SkippedUnrestoredTestProjectIsNotChecked()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"efdoctor-skipped-{Guid.NewGuid():N}");
        var project = await WriteUnrestoredProjectAsync(temporaryDirectory, "UnrestoredTests", usesEfCore: true, isTestProject: true);

        try
        {
            var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");

            Assert.Equal(0, console.ExitCode);
            Assert.DoesNotContain("EFDoctor warning", console.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("Spec", "cli-workspace-loading/Framework list spans several lines")]
    public async Task MultiLineSingleTargetFrameworkProjectIsAnalyzed()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "Workspace.MultiLineTargetFramework", "Workspace.MultiLineTargetFramework.csproj");

        var result = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        // The project references a multi-targeted project whose list also spans lines; both load,
        // so each reports its EFD001 finding.
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToList();
        Assert.All(findings, static finding => Assert.Equal("EFD001", finding.GetProperty("ruleId").GetString()));
        Assert.Equal(
            ["Workspace.MultiLineTargetFramework", "Workspace.MultiLineTargetFrameworks"],
            findings.Select(static finding => Path.GetFileName(Path.GetDirectoryName(finding.GetProperty("sourceFile").GetString())!)).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Spec", "cli-workspace-loading/Framework list with several entries separated by line breaks")]
    public async Task MultiLineMultiTargetFrameworksProjectIsAnalyzed()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "Workspace.MultiLineTargetFrameworks", "Workspace.MultiLineTargetFrameworks.csproj");

        var result = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var finding = Assert.Single(document.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal("EFD001", finding.GetProperty("ruleId").GetString());
    }

    private static async Task<string> WriteUnrestoredProjectAsync(string directory, string name, bool usesEfCore, bool isTestProject = false)
    {
        var projectDirectory = Path.Combine(directory, name);
        Directory.CreateDirectory(projectDirectory);
        var project = Path.Combine(projectDirectory, name + ".csproj");
        var testProperty = isTestProject ? "<IsTestProject>true</IsTestProject>" : string.Empty;
        var efReference = usesEfCore ? "<ItemGroup><PackageReference Include=\"Microsoft.EntityFrameworkCore\" Version=\"10.0.0\" /></ItemGroup>" : string.Empty;
        await File.WriteAllTextAsync(project, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>{testProperty}</PropertyGroup>{efReference}</Project>");
        var source = usesEfCore
            ? "using System.Linq;\nusing Microsoft.EntityFrameworkCore;\n\npublic sealed class Item { public int Id { get; set; } }\npublic sealed class AppContext : DbContext { public DbSet<Item> Items => Set<Item>(); }\npublic static class Cases { public static object All(AppContext context) => context.Items.ToList(); }\n"
            : "public static class Cases { public static int Answer() => 42; }\n";
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Cases.cs"), source);
        return project;
    }

    private static string BuildSolutionFile(string firstProject, string secondProject)
    {
        const string csharpProjectTypeGuid = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";
        var first = Guid.NewGuid().ToString().ToUpperInvariant();
        var second = Guid.NewGuid().ToString().ToUpperInvariant();
        return "Microsoft Visual Studio Solution File, Format Version 12.00\n"
            + "# Visual Studio Version 17\n"
            + $"Project(\"{{{csharpProjectTypeGuid}}}\") = \"Good\", \"{firstProject}\", \"{{{first}}}\"\nEndProject\n"
            + $"Project(\"{{{csharpProjectTypeGuid}}}\") = \"Broken\", \"{secondProject}\", \"{{{second}}}\"\nEndProject\n"
            + "Global\nEndGlobal\n";
    }

    [Fact]
    public async Task Efd014FixtureReportsUnorderedPagination()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD014.Sample", "EFD014.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 2 findings.", console.StandardOutput);
        Assert.Contains("EFD014", console.StandardOutput, StringComparison.Ordinal);

        Assert.Equal(1, json.ExitCode);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD014", finding.GetProperty("ruleId").GetString());
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
        });
    }

    [Fact]
    public async Task Efd021FixtureReportsStaticDbContextField()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD021.Sample", "EFD021.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 1 finding.", console.StandardOutput);
        Assert.Contains("EFD021", console.StandardOutput, StringComparison.Ordinal);

        Assert.Equal(1, json.ExitCode);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var finding = Assert.Single(document.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal("EFD021", finding.GetProperty("ruleId").GetString());
        Assert.Equal("high", finding.GetProperty("confidence").GetString());
        Assert.Equal("warning", finding.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Efd022FixtureReportsStringComparisonInPredicate()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD022.Sample", "EFD022.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 3 findings.", console.StandardOutput);
        Assert.Contains("EFD022", console.StandardOutput, StringComparison.Ordinal);

        Assert.Equal(1, json.ExitCode);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(3, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD022", finding.GetProperty("ruleId").GetString());
            Assert.Equal("high", finding.GetProperty("confidence").GetString());
        });
    }

    [Fact]
    [Trait("Spec", "efd020-multiple-enumeration/Editor configuration suppression")]
    [Trait("Spec", "efd020-multiple-enumeration/Console and JSON reporting")]
    [Trait("Spec", "efd020-multiple-enumeration/CLI end-to-end fixture")]
    public async Task Efd020FixtureReportsMultipleEnumeration()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD020.Sample", "EFD020.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 1 finding.", console.StandardOutput);
        Assert.Contains("EFD020", console.StandardOutput, StringComparison.Ordinal);

        Assert.Equal(1, json.ExitCode);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var finding = Assert.Single(document.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal("EFD020", finding.GetProperty("ruleId").GetString());
        Assert.Equal("medium", finding.GetProperty("confidence").GetString());
        Assert.Equal("warning", finding.GetProperty("severity").GetString());
    }

    [Fact]
    [Trait("Spec", "efd010-sync-db-call-in-async/Editor configuration suppression")]
    [Trait("Spec", "efd010-sync-db-call-in-async/Console and JSON reporting")]
    [Trait("Spec", "efd010-sync-db-call-in-async/CLI end-to-end fixture")]
    public async Task Efd010FixtureReportsSyncCallsInAsyncMethods()
    {
        var project = Path.Combine(RepositoryRoot(), "tests", "Fixtures", "EFD010.Sample", "EFD010.Sample.csproj");

        var console = await RunCliAsync("analyze", project, "--no-color", "--quiet");
        var json = await RunCliAsync("analyze", project, "--format", "json", "--quiet");

        Assert.Equal(1, console.ExitCode);
        Assert.Empty(console.StandardError);
        Assert.Contains("Found 3 findings.", console.StandardOutput);
        Assert.Contains("EFD010", console.StandardOutput, StringComparison.Ordinal);

        Assert.Equal(1, json.ExitCode);
        using var document = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var findings = document.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(3, findings.Length);
        Assert.All(findings, static finding =>
        {
            Assert.Equal("EFD010", finding.GetProperty("ruleId").GetString());
            Assert.Equal("medium", finding.GetProperty("confidence").GetString());
        });
    }

    [Fact]
    public async Task RuntimeScanUsesNoRestoreOrNetworkComponent()
    {
        var root = RepositoryRoot();
        var project = Path.Combine(root, "tests", "Fixtures", "EFD001.Clean", "EFD001.Clean.csproj");
        var productionFiles = Directory.GetFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".csproj", StringComparison.Ordinal));
        // Opting the SDK pre-check's child `dotnet` process out of telemetry is allowed; any other
        // mention of telemetry is not.
        var productionText = string.Join('\n', productionFiles.Select(File.ReadAllText))
            .Replace("DOTNET_CLI_TELEMETRY_OPTOUT", string.Empty, StringComparison.Ordinal);

        var result = await RunCliAsync("analyze", project, "--quiet", "--no-color");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("HttpClient", productionText, StringComparison.Ordinal);
        Assert.DoesNotContain("Telemetry", productionText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet restore", productionText, StringComparison.OrdinalIgnoreCase);
    }

    private static Task<ProcessResult> RunCliAsync(params string[] arguments) =>
        RunCliInAsync(workingDirectory: null, arguments);

    private static async Task<ProcessResult> RunCliInAsync(string? workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }
        startInfo.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the EFDoctor CLI process.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EFDoctor.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the EFDoctor repository root.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private static (int StartLine, int StartColumn, int EndLine, int EndColumn) Range(JsonElement finding)
    {
        var range = finding.GetProperty("range");
        return (
            range.GetProperty("startLine").GetInt32(),
            range.GetProperty("startColumn").GetInt32(),
            range.GetProperty("endLine").GetInt32(),
            range.GetProperty("endColumn").GetInt32());
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
