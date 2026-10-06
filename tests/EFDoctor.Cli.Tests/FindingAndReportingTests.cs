using System.Collections.Immutable;
using System.Text.Json;
using EFDoctor.Analyzers;
using EFDoctor.Cli;
using EFDoctor.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EFDoctor.Cli.Tests;

public sealed class FindingAndReportingTests
{
    [Fact]
    public void FindingRequiresEveryStringFieldAndPositiveRange()
    {
        var finding = CreateFinding("/repo/b.cs", 2, 4, "EFD001");

        Assert.Equal(FindingConfidence.High, finding.Confidence);
        Assert.Equal(2, finding.Range.StartLine);
        Assert.Throws<ArgumentException>(() => new Finding(
            string.Empty,
            finding.RuleTitle,
            finding.Severity,
            finding.Confidence,
            finding.Message,
            finding.SourceFile,
            finding.Range,
            finding.Evidence,
            finding.LikelyImpact,
            finding.SuggestedRemediation,
            finding.DocumentationReference));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceRange(0, 1, 1, 1));
    }

    [Fact]
    public void OrderingIsDeterministicAcrossDiscoveryOrders()
    {
        var findings = new[]
        {
            CreateFinding("/repo/b.cs", 2, 4, "EFD001"),
            CreateFinding("/repo/a.cs", 5, 1, "EFD002"),
            CreateFinding("/repo/a.cs", 5, 1, "EFD001"),
            CreateFinding("/repo/a.cs", 1, 9, "EFD001"),
        };

        var forward = FindingOrder.Apply(findings);
        var reverse = FindingOrder.Apply(findings.Reverse());

        Assert.Equal(forward, reverse);
        Assert.Equal(new[] { 1, 5, 5, 2 }, forward.Select(static finding => finding.Range.StartLine));
        Assert.Equal(new[] { "EFD001", "EFD001", "EFD002", "EFD001" }, forward.Select(static finding => finding.RuleId));
    }

    [Fact]
    public void ConsoleOutputIsCompleteAndNoColorModeHasNoAnsi()
    {
        var output = ConsoleReportRenderer.Render([CreateFinding("/repo/file.cs", 2, 4, "EFD001")], useColor: false);

        Assert.Contains("EFD001", output);
        Assert.Contains("Severity: Warning", output);
        Assert.Contains("Confidence: High", output);
        Assert.Contains("Evidence:", output);
        Assert.Contains("Likely impact:", output);
        Assert.Contains("Suggested remediation:", output);
        Assert.Contains("Documentation: EFD001", output);
        Assert.Contains("Found 1 finding.", output);
        Assert.DoesNotContain('\u001b', output);
        Assert.Contains("No findings found.", ConsoleReportRenderer.Render([], useColor: false));
    }

    [Fact]
    public void JsonOutputHasStableVersionedContract()
    {
        var json = JsonReportRenderer.Render([CreateFinding("/repo/file.cs", 2, 4, "EFD001")]);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        var finding = document.RootElement.GetProperty("findings")[0];
        Assert.Equal("EFD001", finding.GetProperty("ruleId").GetString());
        Assert.Equal("high", finding.GetProperty("confidence").GetString());
        Assert.Equal(2, finding.GetProperty("range").GetProperty("startLine").GetInt32());
        Assert.DoesNotContain('\u001b', json);

        using var empty = JsonDocument.Parse(JsonReportRenderer.Render([]));
        Assert.Equal(0, empty.RootElement.GetProperty("summary").GetProperty("findingCount").GetInt32());
        Assert.Empty(empty.RootElement.GetProperty("findings").EnumerateArray());
    }

    [Fact]
    public void MapperAcceptsEfd002ThroughSharedDiagnosticContract()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { bool M() => true; }", path: "/repo/count.cs");
        var location = Location.Create(tree, new Microsoft.CodeAnalysis.Text.TextSpan(10, 4));
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, "high")
            .Add(DiagnosticPropertyNames.Evidence, "Resolved Queryable.Count is compared as count > 0.")
            .Add(DiagnosticPropertyNames.LikelyImpact, CountUsedForExistenceAnalyzer.Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, "Use Any.")
            .Add(DiagnosticPropertyNames.DocumentationReference, "EFD002");
        var diagnostic = Diagnostic.Create(CountUsedForExistenceAnalyzer.Rule, location, properties);

        var finding = DiagnosticFindingMapper.Map(diagnostic);

        Assert.Equal("EFD002", finding.RuleId);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
        Assert.Equal("EFD002", finding.DocumentationReference);
        Assert.Contains("Queryable.Count", finding.Evidence, StringComparison.Ordinal);
        Assert.Contains("Any", finding.SuggestedRemediation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("high", FindingConfidence.High, FindingSeverity.Warning)]
    [InlineData("medium", FindingConfidence.Medium, FindingSeverity.Warning)]
    [InlineData("advisory", FindingConfidence.Advisory, FindingSeverity.Info)]
    public void MapperDerivesSeverityFromConfidence(
        string confidence,
        FindingConfidence expectedConfidence,
        FindingSeverity expectedSeverity)
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { } }", path: "/repo/materialize.cs");
        var location = Location.Create(tree, new Microsoft.CodeAnalysis.Text.TextSpan(10, 4));
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, confidence)
            .Add(DiagnosticPropertyNames.Evidence, "A proven inline EF query has no strong row bound.")
            .Add(DiagnosticPropertyNames.LikelyImpact, UnboundedQueryMaterializationAnalyzer.Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, "Review whether every row is needed.")
            .Add(DiagnosticPropertyNames.DocumentationReference, "EFD005");
        var diagnostic = Diagnostic.Create(UnboundedQueryMaterializationAnalyzer.Rule, location, properties);

        var finding = DiagnosticFindingMapper.Map(diagnostic);

        Assert.Equal(expectedConfidence, finding.Confidence);
        Assert.Equal(expectedSeverity, finding.Severity);
    }

    // In a build, medium-confidence diagnostics are capped at Info. Reports keep them as warnings.
    [Theory]
    [InlineData("EFD005", "medium", DiagnosticSeverity.Info, FindingSeverity.Warning)]
    [InlineData("EFD005", "medium", DiagnosticSeverity.Warning, FindingSeverity.Warning)]
    [InlineData("EFD005", "medium", DiagnosticSeverity.Error, FindingSeverity.Error)]
    [InlineData("EFD005", "high", DiagnosticSeverity.Warning, FindingSeverity.Warning)]
    [InlineData("EFD005", "advisory", DiagnosticSeverity.Info, FindingSeverity.Info)]
    [InlineData("EFD025", "high", DiagnosticSeverity.Info, FindingSeverity.Info)]
    public void MapperKeepsReportSeverityIndependentOfTheBuildCap(
        string ruleId,
        string confidence,
        DiagnosticSeverity buildSeverity,
        FindingSeverity expectedSeverity)
    {
        var rule = ruleId == "EFD025" ? RedundantIncludeAnalyzer.Rule : UnboundedQueryMaterializationAnalyzer.Rule;
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { } }", path: "/repo/materialize.cs");
        var location = Location.Create(tree, new Microsoft.CodeAnalysis.Text.TextSpan(10, 4));
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, confidence)
            .Add(DiagnosticPropertyNames.Evidence, "Evidence.")
            .Add(DiagnosticPropertyNames.LikelyImpact, "Impact.")
            .Add(DiagnosticPropertyNames.SuggestedRemediation, "Remediation.")
            .Add(DiagnosticPropertyNames.DocumentationReference, ruleId);
        var diagnostic = Diagnostic.Create(rule, location, buildSeverity, additionalLocations: null, properties);

        Assert.Equal(expectedSeverity, DiagnosticFindingMapper.Map(diagnostic).Severity);
    }

    [Fact]
    public void MixedRuleReportsPreserveSchemaAndRuleIdTieBreak()
    {
        var findings = new[]
        {
            CreateFinding("/repo/file.cs", 2, 4, "EFD002"),
            CreateFinding("/repo/file.cs", 2, 4, "EFD001"),
        };

        var console = ConsoleReportRenderer.Render(findings, useColor: false);
        using var json = JsonDocument.Parse(JsonReportRenderer.Render(findings));

        Assert.True(console.IndexOf("EFD001", StringComparison.Ordinal) < console.IndexOf("EFD002", StringComparison.Ordinal));
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        var rendered = json.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(new[] { "EFD001", "EFD002" }, rendered.Select(static item => item.GetProperty("ruleId").GetString()));
    }

    private static Finding CreateFinding(string path, int line, int column, string ruleId)
    {
        return new Finding(
            ruleId,
            ruleId == "EFD002" ? "Query result used only to test existence" : "SaveChanges executed inside a loop",
            FindingSeverity.Warning,
            FindingConfidence.High,
            "This EF Core save executes inside a loop and may cause repeated database round trips.",
            path,
            new SourceRange(line, column, line, column + 10),
            "The resolved EF Core invocation is inside a foreach loop.",
            "Saving within a loop can create repeated database round trips and prevent effective batching.",
            "Batch when appropriate; retain intentional transaction boundaries.",
            ruleId);
    }
}
