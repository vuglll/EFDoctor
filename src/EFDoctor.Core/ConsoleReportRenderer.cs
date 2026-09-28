using System.Text;

namespace EFDoctor.Core;

public static class ConsoleReportRenderer
{
    private const string BoldYellow = "\u001b[1;33m";
    private const string Reset = "\u001b[0m";

    public static string Render(IEnumerable<Finding> findings, bool useColor)
    {
        var ordered = FindingOrder.Apply(findings);
        if (ordered.Count == 0)
        {
            return "No findings found." + Environment.NewLine;
        }

        var builder = new StringBuilder();
        foreach (var finding in ordered)
        {
            var rule = useColor ? $"{BoldYellow}{finding.RuleId}{Reset}" : finding.RuleId;
            builder.Append(FindingOrder.NormalizePath(finding.SourceFile))
                .Append('(').Append(finding.Range.StartLine).Append(',').Append(finding.Range.StartColumn)
                .Append(")-(").Append(finding.Range.EndLine).Append(',').Append(finding.Range.EndColumn)
                .AppendLine(")")
                .Append("  ").Append(rule).Append(": ").AppendLine(finding.RuleTitle)
                .Append("  Severity: ").Append(finding.Severity)
                .Append(" | Confidence: ").AppendLine(finding.Confidence.ToString())
                .Append("  Message: ").AppendLine(finding.Message)
                .Append("  Evidence: ").AppendLine(finding.Evidence)
                .Append("  Likely impact: ").AppendLine(finding.LikelyImpact)
                .Append("  Suggested remediation: ").AppendLine(finding.SuggestedRemediation)
                .Append("  Documentation: ").AppendLine(finding.DocumentationReference)
                .AppendLine();
        }

        builder.Append("Found ").Append(ordered.Count).Append(ordered.Count == 1 ? " finding." : " findings.").AppendLine();
        return builder.ToString();
    }
}
