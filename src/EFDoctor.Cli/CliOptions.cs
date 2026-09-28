namespace EFDoctor.Cli;

public enum OutputFormat
{
    Console,
    Json,
}

public sealed record CliOptions(string TargetPath, OutputFormat Format, bool Quiet, bool NoColor, bool IncludeTestProjects)
{
    public static bool TryParse(string[] args, out CliOptions? options, out string? error, out OutputFormat requestedFormat)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        error = null;
        requestedFormat = OutputFormat.Console;

        if (args.Length == 0 || !string.Equals(args[0], "analyze", StringComparison.Ordinal))
        {
            error = ToolInfo.UsageLine;
            return false;
        }

        string? targetPath = null;
        var quiet = false;
        var noColor = false;
        var includeTestProjects = false;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--quiet":
                    quiet = true;
                    break;
                case "--no-color":
                    noColor = true;
                    break;
                case "--include-test-projects":
                    includeTestProjects = true;
                    break;
                case "--format":
                    if (++index >= args.Length)
                    {
                        error = "Option '--format' requires 'console' or 'json'.";
                        return false;
                    }

                    if (string.Equals(args[index], "json", StringComparison.OrdinalIgnoreCase))
                    {
                        requestedFormat = OutputFormat.Json;
                    }
                    else if (string.Equals(args[index], "console", StringComparison.OrdinalIgnoreCase))
                    {
                        requestedFormat = OutputFormat.Console;
                    }
                    else
                    {
                        error = $"Unsupported output format '{args[index]}'. Use 'console' or 'json'.";
                        return false;
                    }

                    break;
                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"Unknown option '{argument}'.";
                        return false;
                    }

                    if (targetPath is not null)
                    {
                        error = "Only one solution or project path can be analyzed at a time.";
                        return false;
                    }

                    targetPath = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            error = "A solution or project path is required.";
            return false;
        }

        options = new CliOptions(targetPath, requestedFormat, quiet, noColor || requestedFormat == OutputFormat.Json, includeTestProjects);
        return true;
    }
}
