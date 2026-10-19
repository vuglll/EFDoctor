using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFDoctor.Cli;

// Analyzer options for one project in a workspace scan: the project's own options, plus global
// options that only the whole scan can know. A test project that --include-test-projects brought
// into the scan gets the analyzers' opt-in, and every project learns whether any project in the
// scan configures split queries by default.
internal static class CliAnalyzerOptions
{
    public static AnalyzerOptions For(AnalyzerOptions options, bool optInTestProject, bool splitQueryByDefault)
    {
        var added = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (optInTestProject)
        {
            added[EfTestProjectDetection.AnalyzeTestProjectsProperty] = bool.TrueString;
        }

        if (splitQueryByDefault)
        {
            added[EfSplitQueryConfiguration.SplitQueryByDefaultOption] = bool.TrueString;
        }

        return added.Count == 0
            ? options
            : new(options.AdditionalFiles, new Provider(options.AnalyzerConfigOptionsProvider, added));
    }

    private sealed class Provider(AnalyzerConfigOptionsProvider inner, IReadOnlyDictionary<string, string> added) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(inner.GlobalOptions, added);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => inner.GetOptions(tree);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => inner.GetOptions(textFile);
    }

    private sealed class Options(AnalyzerConfigOptions inner, IReadOnlyDictionary<string, string> added) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (added.TryGetValue(key, out var addedValue))
            {
                value = addedValue;
                return true;
            }

            return inner.TryGetValue(key, out value!);
        }

        public override IEnumerable<string> Keys => inner.Keys.Concat(added.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
