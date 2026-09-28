using System.Text.Json;
using EFDoctor.Core;

namespace EFDoctor.Cli;

public static class CliApplication
{
    public const int SuccessNoFindings = 0;
    public const int SuccessWithFindings = 1;
    public const int InvalidInputOrAnalysisFailure = 2;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj",
        ".sln",
        ".slnx",
    };

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter errorOutput,
        CancellationToken cancellationToken)
    {
        if (args.Length == 1 && args[0] == "--version")
        {
            await output.WriteLineAsync(ToolInfo.Version).ConfigureAwait(false);
            return SuccessNoFindings;
        }

        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            await output.WriteAsync(ToolInfo.Help).ConfigureAwait(false);
            return SuccessNoFindings;
        }

        if (!CliOptions.TryParse(args, out var options, out var parseError, out var requestedFormat))
        {
            WriteError(parseError!, "invalid-input", requestedFormat, output, errorOutput);
            return InvalidInputOrAnalysisFailure;
        }

        var validationError = ValidateTarget(options!.TargetPath);
        if (validationError is not null)
        {
            WriteError(validationError, "invalid-input", options.Format, output, errorOutput);
            return InvalidInputOrAnalysisFailure;
        }

        try
        {
            var targetPath = Path.GetFullPath(options.TargetPath);
            if (!MsBuildRegistration.TryRegisterFor(Path.GetDirectoryName(targetPath)!, out var registrationError))
            {
                WriteError(registrationError!, "analysis-failure", options.Format, output, errorOutput);
                return InvalidInputOrAnalysisFailure;
            }

            var analysis = await WorkspaceAnalyzer.AnalyzeAsync(targetPath, cancellationToken, options.IncludeTestProjects).ConfigureAwait(false);

            if (!analysis.Succeeded)
            {
                WriteError(analysis.Error!, "analysis-failure", options.Format, output, errorOutput);
                return InvalidInputOrAnalysisFailure;
            }

            // Warnings go to standard error in every mode so standard output stays a pure report.
            foreach (var warning in analysis.Warnings)
            {
                await errorOutput.WriteLineAsync($"EFDoctor warning: {warning}").ConfigureAwait(false);
            }

            var findings = FindingOrder.Apply(analysis.Findings);
            if (options.Format == OutputFormat.Json)
            {
                await output.WriteLineAsync(JsonReportRenderer.Render(findings)).ConfigureAwait(false);
            }
            else
            {
                await output.WriteAsync(ConsoleReportRenderer.Render(findings, !options.NoColor)).ConfigureAwait(false);
            }

            return findings.Count == 0 ? SuccessNoFindings : SuccessWithFindings;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var message = MsBuildRegistration.IsSdkResolutionFailure(exception.Message)
                ? MsBuildRegistration.DescribeSdkResolutionFailure(Path.GetDirectoryName(Path.GetFullPath(options.TargetPath))!, exception.Message)
                : $"Analysis failed: {exception.Message}";
            WriteError(message, "analysis-failure", options.Format, output, errorOutput);
            return InvalidInputOrAnalysisFailure;
        }
    }

    public static string? ValidateTarget(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return $"The supplied path does not exist or is not a file: {targetPath}";
        }

        var extension = Path.GetExtension(targetPath);
        if (!SupportedExtensions.Contains(extension))
        {
            return $"Unsupported input '{extension}'. Supply a .sln, .slnx, or .csproj file.";
        }

        return null;
    }

    private static void WriteError(
        string message,
        string code,
        OutputFormat format,
        TextWriter output,
        TextWriter errorOutput)
    {
        if (format == OutputFormat.Json)
        {
            var payload = new ErrorEnvelope(ReportEnvelope.CurrentSchemaVersion, new ErrorDetail(code, message));
            output.WriteLine(JsonSerializer.Serialize(payload, JsonReportRenderer.SerializerOptions));
            return;
        }

        errorOutput.WriteLine($"EFDoctor error: {message}");
    }

    private sealed record ErrorEnvelope(int SchemaVersion, ErrorDetail Error);

    private sealed record ErrorDetail(string Code, string Message);
}
