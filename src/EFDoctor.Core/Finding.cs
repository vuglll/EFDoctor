namespace EFDoctor.Core;

public enum FindingSeverity
{
    Info,
    Warning,
    Error,
}

public enum FindingConfidence
{
    Advisory,
    Medium,
    High,
}

public sealed record SourceRange
{
    public SourceRange(int startLine, int startColumn, int endLine, int endColumn)
    {
        if (startLine < 1 || startColumn < 1 || endLine < 1 || endColumn < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine), "Source coordinates are one-based and must be positive.");
        }

        if (endLine < startLine || (endLine == startLine && endColumn < startColumn))
        {
            throw new ArgumentException("The source range end must not precede its start.");
        }

        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    public int StartLine { get; }

    public int StartColumn { get; }

    public int EndLine { get; }

    public int EndColumn { get; }
}

public sealed record Finding
{
    public Finding(
        string ruleId,
        string ruleTitle,
        FindingSeverity severity,
        FindingConfidence confidence,
        string message,
        string sourceFile,
        SourceRange range,
        string evidence,
        string likelyImpact,
        string suggestedRemediation,
        string documentationReference)
    {
        RuleId = Required(ruleId, nameof(ruleId));
        RuleTitle = Required(ruleTitle, nameof(ruleTitle));
        Severity = severity;
        Confidence = confidence;
        Message = Required(message, nameof(message));
        SourceFile = Required(sourceFile, nameof(sourceFile));
        Range = range ?? throw new ArgumentNullException(nameof(range));
        Evidence = Required(evidence, nameof(evidence));
        LikelyImpact = Required(likelyImpact, nameof(likelyImpact));
        SuggestedRemediation = Required(suggestedRemediation, nameof(suggestedRemediation));
        DocumentationReference = Required(documentationReference, nameof(documentationReference));
    }

    public string RuleId { get; }

    public string RuleTitle { get; }

    public FindingSeverity Severity { get; }

    public FindingConfidence Confidence { get; }

    public string Message { get; }

    public string SourceFile { get; }

    public SourceRange Range { get; }

    public string Evidence { get; }

    public string LikelyImpact { get; }

    public string SuggestedRemediation { get; }

    public string DocumentationReference { get; }

    private static string Required(string value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A finding field cannot be empty.", parameterName)
            : value;
    }
}

public sealed record ScanSummary(int FindingCount);

public sealed record ReportEnvelope(int SchemaVersion, ScanSummary Summary, IReadOnlyList<Finding> Findings)
{
    public const int CurrentSchemaVersion = 1;

    public static ReportEnvelope Create(IEnumerable<Finding> findings)
    {
        var ordered = FindingOrder.Apply(findings);
        return new ReportEnvelope(CurrentSchemaVersion, new ScanSummary(ordered.Count), ordered);
    }
}
