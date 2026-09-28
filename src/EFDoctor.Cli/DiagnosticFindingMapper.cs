using EFDoctor.Analyzers;
using EFDoctor.Core;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Cli;

public static class DiagnosticFindingMapper
{
    public static Finding Map(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree is null)
        {
            throw new ArgumentException("Only source diagnostics can be mapped to findings.", nameof(diagnostic));
        }

        var lineSpan = diagnostic.Location.GetLineSpan();
        var sourceFile = diagnostic.Location.SourceTree.FilePath;
        if (string.IsNullOrWhiteSpace(sourceFile))
        {
            sourceFile = lineSpan.Path;
        }

        var confidence = ParseConfidence(GetRequiredProperty(diagnostic, DiagnosticPropertyNames.Confidence));
        return new Finding(
            diagnostic.Id,
            diagnostic.Descriptor.Title.ToString(),
            confidence == FindingConfidence.Advisory ? FindingSeverity.Info : MapSeverity(diagnostic.Severity),
            confidence,
            diagnostic.GetMessage(),
            FindingOrder.NormalizePath(sourceFile),
            new SourceRange(
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1,
                lineSpan.EndLinePosition.Line + 1,
                lineSpan.EndLinePosition.Character + 1),
            GetRequiredProperty(diagnostic, DiagnosticPropertyNames.Evidence),
            GetRequiredProperty(diagnostic, DiagnosticPropertyNames.LikelyImpact),
            GetRequiredProperty(diagnostic, DiagnosticPropertyNames.SuggestedRemediation),
            GetRequiredProperty(diagnostic, DiagnosticPropertyNames.DocumentationReference));
    }

    private static FindingSeverity MapSeverity(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Error => FindingSeverity.Error,
            DiagnosticSeverity.Warning => FindingSeverity.Warning,
            _ => FindingSeverity.Info,
        };
    }

    private static FindingConfidence ParseConfidence(string confidence)
    {
        return confidence switch
        {
            "high" => FindingConfidence.High,
            "medium" => FindingConfidence.Medium,
            _ => FindingConfidence.Advisory,
        };
    }

    private static string GetRequiredProperty(Diagnostic diagnostic, string key)
    {
        return diagnostic.Properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Diagnostic {diagnostic.Id} is missing required property '{key}'.");
    }
}
