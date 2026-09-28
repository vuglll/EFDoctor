using System.Text.Json;
using EFDoctor.Cli;

namespace EFDoctor.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public void ParsesAllAutomationOptions()
    {
        var parsed = CliOptions.TryParse(
            ["analyze", "sample.csproj", "--format", "json", "--quiet", "--no-color"],
            out var options,
            out var error,
            out var requestedFormat);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.Equal(OutputFormat.Json, requestedFormat);
        Assert.True(options.Quiet);
        Assert.True(options.NoColor);
        Assert.False(options.IncludeTestProjects);
    }

    [Fact]
    public void IncludeTestProjectsFlagParses()
    {
        var withFlag = CliOptions.TryParse(
            ["analyze", "sample.csproj", "--include-test-projects"],
            out var options,
            out var error,
            out _);

        Assert.True(withFlag, error);
        Assert.NotNull(options);
        Assert.True(options.IncludeTestProjects);

        var withoutFlag = CliOptions.TryParse(
            ["analyze", "sample.csproj"],
            out var defaultOptions,
            out _,
            out _);

        Assert.True(withoutFlag);
        Assert.NotNull(defaultOptions);
        Assert.False(defaultOptions.IncludeTestProjects);
    }

    [Theory]
    [InlineData()]
    [InlineData("analyze")]
    [InlineData("analyze", "sample.txt")]
    [InlineData("scan", "sample.csproj")]
    public async Task InvalidInputsReturnExitCodeTwo(params string[] args)
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = await CliApplication.RunAsync(args, output, errors, CancellationToken.None);

        Assert.Equal(CliApplication.InvalidInputOrAnalysisFailure, exitCode);
        Assert.NotEmpty(errors.ToString());
    }

    [Fact]
    public async Task JsonValidationErrorIsMachineReadableAndUsesOnlyStdout()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["analyze", "/definitely/missing.csproj", "--format", "json"],
            output,
            errors,
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Empty(errors.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("invalid-input", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain('\u001b', output.ToString());
    }

    [Fact]
    public async Task VersionPrintsPackageVersionWithoutBuildMetadata()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["--version"], output, errors, CancellationToken.None);

        Assert.Equal(CliApplication.SuccessNoFindings, exitCode);
        Assert.Empty(errors.ToString());
        var version = output.ToString().Trim();
        Assert.Equal(ToolInfo.Version, version);
        Assert.DoesNotContain('+', version);
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task HelpPrintsUsageOptionsAndExitCodes(string flag)
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = await CliApplication.RunAsync([flag], output, errors, CancellationToken.None);

        Assert.Equal(CliApplication.SuccessNoFindings, exitCode);
        Assert.Empty(errors.ToString());
        var help = output.ToString();
        Assert.Contains(ToolInfo.UsageLine, help, StringComparison.Ordinal);
        Assert.Contains("--format console|json", help, StringComparison.Ordinal);
        Assert.Contains("--version", help, StringComparison.Ordinal);
        Assert.Contains("2  Invalid input", help, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesAnSdkForDirectoryWithoutGlobalJson()
    {
        using var directory = new TemporaryDirectory();

        Assert.True(MsBuildRegistration.TryResolveSdk(directory.Path, out var instance, out var error), error);
        Assert.NotNull(instance.Version);
    }

    [Fact]
    public void HonorsGlobalJsonPinningAnInstalledSdk()
    {
        using var probe = new TemporaryDirectory();
        Assert.True(MsBuildRegistration.TryResolveSdk(probe.Path, out _, out var probeError), probeError);
        var installed = Microsoft.Build.Locator.MSBuildLocator.QueryVisualStudioInstances(
                new Microsoft.Build.Locator.VisualStudioInstanceQueryOptions
                {
                    DiscoveryTypes = Microsoft.Build.Locator.DiscoveryType.DotNetSdk,
                    WorkingDirectory = probe.Path,
                })
            .Select(static instance => instance.Version)
            .OrderBy(static version => version)
            .First();
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "global.json"),
            $$"""{ "sdk": { "version": "{{installed}}", "rollForward": "disable" } }""");

        Assert.True(MsBuildRegistration.TryResolveSdk(directory.Path, out var instance, out var error), error);
        Assert.Equal(installed, instance.Version);
    }

    [Fact]
    public void ReportsGlobalJsonPinningAMissingSdk()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "global.json"),
            """{ "sdk": { "version": "99.0.100", "rollForward": "disable" } }""");

        Assert.False(MsBuildRegistration.TryResolveSdk(directory.Path, out _, out var error));
        Assert.Contains("No installed .NET SDK satisfies the global.json", error, StringComparison.Ordinal);
        Assert.Contains(directory.Path, error, StringComparison.Ordinal);
        Assert.Contains("requested SDK 99.0.100", error, StringComparison.Ordinal);
        Assert.DoesNotContain("hostfxr", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TargetPinningMissingSdkFailsWithActionableJsonError()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "global.json"),
            """{ "sdk": { "version": "99.0.100", "rollForward": "disable" } }""");
        var project = Path.Combine(directory.Path, "Pinned.csproj");
        File.WriteAllText(project, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["analyze", project, "--format", "json"], output, errors, CancellationToken.None);

        Assert.Equal(CliApplication.InvalidInputOrAnalysisFailure, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("analysis-failure", error.GetProperty("code").GetString());
        Assert.Contains("No installed .NET SDK satisfies the global.json", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("99.0.100", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"efdoctor-sdk-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
