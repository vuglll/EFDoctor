namespace EFDoctor.Core;

public static class FindingOrder
{
    public static IReadOnlyList<Finding> Apply(IEnumerable<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        return findings
            .OrderBy(static finding => NormalizePath(finding.SourceFile), StringComparer.Ordinal)
            .ThenBy(static finding => finding.Range.StartLine)
            .ThenBy(static finding => finding.Range.StartColumn)
            .ThenBy(static finding => finding.Range.EndLine)
            .ThenBy(static finding => finding.Range.EndColumn)
            .ThenBy(static finding => finding.RuleId, StringComparer.Ordinal)
            .ToArray();
    }

    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path).Replace('\\', '/');
    }
}
