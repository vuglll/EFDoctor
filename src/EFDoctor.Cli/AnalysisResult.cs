using EFDoctor.Core;

namespace EFDoctor.Cli;

public sealed record AnalysisResult(bool Succeeded, IReadOnlyList<Finding> Findings, string? Error)
{
    // Non-fatal problems the user must see, such as a project whose EF Core types did not resolve.
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static AnalysisResult Success(IEnumerable<Finding> findings, IReadOnlyList<string>? warnings = null) =>
        new(true, FindingOrder.Apply(findings), null) { Warnings = warnings ?? Array.Empty<string>() };

    public static AnalysisResult Failure(string error) => new(false, Array.Empty<Finding>(), error);
}
