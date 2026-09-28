using System.Reflection;

namespace EFDoctor.Cli;

public static class ToolInfo
{
    public const string UsageLine = "Usage: efdoctor analyze <solution-or-project-path> [--format console|json] [--quiet] [--no-color]";

    public static string Version
    {
        get
        {
            var informational = typeof(ToolInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "0.0.0";

            // Drop build metadata such as "+<commit>" so the output matches the package version.
            var metadataStart = informational.IndexOf('+', StringComparison.Ordinal);
            return metadataStart < 0 ? informational : informational[..metadataStart];
        }
    }

    public static string Help =>
        $"""
        EFDoctor {Version}
        Local-first performance and safety diagnostics for EF Core.

        {UsageLine}
               efdoctor --version
               efdoctor --help

        Options:
          --format console|json    Report format (default: console). JSON implies --no-color.
          --quiet                  Omit nonessential progress and decoration.
          --no-color               Omit ANSI color sequences.
          --include-test-projects  Analyze test projects too (skipped by default);
                                   their findings are reported at advisory/Info.
          --version                Print the tool version.
          -h, --help               Print this help.

        Exit codes:
          0  Analysis completed with no findings.
          1  Analysis completed with one or more findings.
          2  Invalid input, or the target could not be analyzed.

        The target must be restored before analysis. A project that uses EF Core but whose
        EF Core types don't resolve is not analyzed and gets a warning on standard error;
        if no EF Core project resolves, the run fails with exit code 2.

        Loading the target runs the project's MSBuild logic, so analyze only repositories
        you trust.

        """;
}
