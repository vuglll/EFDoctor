using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFDoctor.Cli;

// Analyzer options for a test project that --include-test-projects brought into the scan:
// the project's own options, plus the analyzers' opt-in for test projects.
internal static class TestProjectAnalyzerOptions
{
    public static AnalyzerOptions OptIn(AnalyzerOptions options) =>
        new(options.AdditionalFiles, new OptInProvider(options.AnalyzerConfigOptionsProvider));

    private sealed class OptInProvider(AnalyzerConfigOptionsProvider inner) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new OptInOptions(inner.GlobalOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => inner.GetOptions(tree);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => inner.GetOptions(textFile);
    }

    private sealed class OptInOptions(AnalyzerConfigOptions inner) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (string.Equals(key, EfTestProjectDetection.AnalyzeTestProjectsProperty, StringComparison.OrdinalIgnoreCase))
            {
                value = bool.TrueString;
                return true;
            }

            return inner.TryGetValue(key, out value!);
        }

        public override IEnumerable<string> Keys => inner.Keys.Append(EfTestProjectDetection.AnalyzeTestProjectsProperty);
    }
}
