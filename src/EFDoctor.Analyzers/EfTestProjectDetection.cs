using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFDoctor.Analyzers;

// One classification of test projects for the analyzers and the CLI.
public static class EfTestProjectDetection
{
    public const string IsTestProjectProperty = "build_property.IsTestProject";
    public const string AnalyzeTestProjectsProperty = "build_property.EFDoctorAnalyzeTestProjects";

    public static bool IsMarkedTestProject(AnalyzerConfigOptions globalOptions) =>
        IsTrue(globalOptions, IsTestProjectProperty);

    public static bool AnalyzesTestProjects(AnalyzerConfigOptions globalOptions) =>
        IsTrue(globalOptions, AnalyzeTestProjectsProperty);

    public static bool ReferencesTestFramework(Compilation compilation)
    {
        foreach (var assembly in compilation.ReferencedAssemblyNames)
        {
            if (IsTestFrameworkAssembly(assembly.Name))
            {
                return true;
            }
        }

        return false;
    }

    // A framework's core assembly, not a library that only ships alongside one: production code
    // can reference `xunit.assert` or `xunit.abstractions` without being a test project.
    private static bool IsTestFrameworkAssembly(string name) =>
        string.Equals(name, "xunit", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "xunit.core", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("xunit.v3.core", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "nunit.framework", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrue(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out var value)
        && bool.TryParse(value, out var parsed)
        && parsed;
}
