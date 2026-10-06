using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFDoctor.Analyzers;

internal static class EfAnalysisScope
{
    // Test projects are out of scope unless EFDoctorAnalyzeTestProjects opts them back in.
    public static bool Includes(AnalyzerOptions options, Compilation compilation)
    {
        var globalOptions = options.AnalyzerConfigOptionsProvider.GlobalOptions;
        return EfTestProjectDetection.AnalyzesTestProjects(globalOptions)
            || !(EfTestProjectDetection.IsMarkedTestProject(globalOptions)
                || EfTestProjectDetection.ReferencesTestFramework(compilation));
    }
}
