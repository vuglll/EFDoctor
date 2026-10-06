using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers;

// Every rule reports through here, so that a finding's severity in a build follows its
// confidence: only a high-confidence finding keeps the rule's default severity.
internal static class EfDiagnostic
{
    public static Diagnostic Create(
        DiagnosticDescriptor rule,
        Location location,
        ImmutableDictionary<string, string?> properties,
        params object?[] messageArgs)
    {
        properties.TryGetValue(DiagnosticPropertyNames.Confidence, out var confidence);
        return Diagnostic.Create(
            rule,
            location,
            BuildSeverity(rule.DefaultSeverity, confidence),
            additionalLocations: null,
            properties,
            messageArgs);
    }

    // Medium-confidence and advisory findings are judgment calls, so they never fail a
    // warnings-as-errors build by default. A severity configured for the rule still applies.
    internal static DiagnosticSeverity BuildSeverity(DiagnosticSeverity ruleSeverity, string? confidence) =>
        confidence == "high" || ruleSeverity < DiagnosticSeverity.Info
            ? ruleSeverity
            : DiagnosticSeverity.Info;
}
