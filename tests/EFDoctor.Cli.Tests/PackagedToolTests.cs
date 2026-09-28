using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using EFDoctor.Cli;

namespace EFDoctor.Cli.Tests;

public sealed class PackagedToolTests
{
    [Fact]
    public async Task PackedToolInstallsFromLocalSourceAndMatchesRepositoryCli()
    {
        var root = RepositoryRoot();
        var workDirectory = Path.Combine(Path.GetTempPath(), $"efdoctor-pack-{Guid.NewGuid():N}");
        var packageDirectory = Path.Combine(workDirectory, "packages");
        var toolDirectory = Path.Combine(workDirectory, "tools");
        Directory.CreateDirectory(packageDirectory);

        try
        {
            // Pack the CLI that the test run already built, in the same configuration.
            var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                ? "Release"
                : "Debug";
            var pack = await RunAsync(
                "dotnet",
                root,
                "pack", Path.Combine(root, "src", "EFDoctor.Cli", "EFDoctor.Cli.csproj"),
                "--no-build", "--configuration", configuration, "--output", packageDirectory);
            Assert.True(pack.ExitCode == 0, pack.StandardOutput + pack.StandardError);

            var package = Assert.Single(Directory.GetFiles(packageDirectory, "*.nupkg"));
            Assert.Single(Directory.GetFiles(packageDirectory, "*.snupkg"));
            AssertPackageContents(package);

            // Install from an isolated source and package cache so the test makes no network
            // calls and never reuses a stale cached copy of the same version.
            var nugetConfig = Path.Combine(workDirectory, "nuget.config");
            await File.WriteAllTextAsync(nugetConfig, $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="{Path.Combine(workDirectory, "cache")}" />
                  </config>
                  <packageSources>
                    <clear />
                    <add key="local" value="{packageDirectory}" />
                  </packageSources>
                </configuration>
                """);
            var install = await RunAsync(
                "dotnet",
                workDirectory,
                "tool", "install", "EFDoctor",
                "--tool-path", toolDirectory,
                "--configfile", nugetConfig,
                "--version", ToolInfo.Version);
            Assert.True(install.ExitCode == 0, install.StandardOutput + install.StandardError);

            var command = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? "efdoctor.exe" : "efdoctor");
            var version = await RunAsync(command, workDirectory, "--version");
            Assert.Equal(0, version.ExitCode);
            Assert.Equal(ToolInfo.Version, version.StandardOutput.Trim());

            var sample = Path.Combine(root, "tests", "Fixtures", "EFD001.Sample", "EFD001.Sample.csproj");
            var installed = await RunAsync(command, workDirectory, "analyze", sample, "--format", "json", "--quiet");
            var repository = await RunAsync("dotnet", root, typeof(CliApplication).Assembly.Location, "analyze", sample, "--format", "json", "--quiet");

            Assert.Equal(1, installed.ExitCode);
            Assert.Empty(installed.StandardError);
            Assert.Equal(repository.ExitCode, installed.ExitCode);
            Assert.Equal(repository.StandardOutput, installed.StandardOutput);
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    private static void AssertPackageContents(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Select(static entry => entry.FullName).ToArray();

        foreach (var required in new[] { "NOTICE", "THIRD-PARTY-NOTICES.md", "PACKAGE.md", "icon.png", "CHANGELOG.md" })
        {
            Assert.Contains(required, entries);
        }

        Assert.Contains(entries, static entry => entry.StartsWith("tools/net10.0/any/BuildHost-netcore/", StringComparison.Ordinal));
        Assert.Contains("tools/net10.0/any/efdoctor.dll", entries);
        Assert.Contains("tools/net10.0/any/build/EFDoctor.Workspace.targets", entries);

        // MSBuild itself must come from the installed SDK; only the locator may be bundled.
        Assert.DoesNotContain(entries, static entry =>
        {
            var name = Path.GetFileName(entry);
            return name.StartsWith("Microsoft.Build.", StringComparison.Ordinal)
                && name.EndsWith(".dll", StringComparison.Ordinal)
                && name != "Microsoft.Build.Locator.dll";
        });

        using var nuspecStream = archive.GetEntry("EFDoctor.nuspec")!.Open();
        var nuspec = XDocument.Load(nuspecStream);
        var metadata = nuspec.Root!.Elements().Single(static element => element.Name.LocalName == "metadata");
        string? Value(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value;

        Assert.Equal("EFDoctor", Value("id"));
        Assert.Equal(ToolInfo.Version, Value("version"));
        Assert.Equal("Apache-2.0", Value("license"));
        Assert.Equal("icon.png", Value("icon"));
        Assert.Equal("PACKAGE.md", Value("readme"));

        // URLs appear only when Directory.Build.props sets EFDoctorRepositoryUrl.
        var configuredUrl = XDocument.Load(Path.Combine(RepositoryRoot(), "Directory.Build.props"))
            .Descendants()
            .SingleOrDefault(static element => element.Name.LocalName == "EFDoctorRepositoryUrl")
            ?.Value;
        var expectedUrl = string.IsNullOrWhiteSpace(configuredUrl) ? null : configuredUrl.Trim();
        Assert.Equal(expectedUrl, Value("projectUrl"));
        var repository = metadata.Elements().FirstOrDefault(static element => element.Name.LocalName == "repository");
        Assert.Equal(expectedUrl, repository?.Attribute("url")?.Value);
        Assert.Contains(
            metadata.Descendants().Where(static element => element.Name.LocalName == "packageType"),
            static packageType => packageType.Attribute("name")?.Value == "DotnetTool");
    }

    private static async Task<ProcessResult> RunAsync(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EFDoctor.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the EFDoctor repository root.");
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
