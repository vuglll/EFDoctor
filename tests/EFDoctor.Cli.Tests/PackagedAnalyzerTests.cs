using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using EFDoctor.Cli;

namespace EFDoctor.Cli.Tests;

// Packs EFDoctor.Analyzers and builds a real project that references it
// (openspec/specs/analyzer-package).
public sealed class PackagedAnalyzerTests
{
    private const string Subject = """
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        public sealed class Customer
        {
            public int Id { get; set; }
            public string Email { get; set; } = "";
        }

        public sealed class ShopContext : DbContext
        {
            public DbSet<Customer> Customers => Set<Customer>();
        }

        public static class Subject
        {
            public static void Save(ShopContext context)
            {
                foreach (var i in new[] { 1 })
                {
                    context.SaveChanges();
                }
            }

            public static object Find(ShopContext context, string email) =>
                context.Customers.Where(customer => customer.Email.ToLower() == email);
        }
        """;

    [Fact]
    [Trait("Spec", "analyzer-package/Package contents")]
    [Trait("Spec", "analyzer-package/Referenced through central package management")]
    [Trait("Spec", "analyzer-package/Suggestions do not fail a build")]
    [Trait("Spec", "analyzer-package/Test project built with the package")]
    public async Task PackedAnalyzersRunInABuildThroughCentralPackageManagement()
    {
        var root = RepositoryRoot();
        var workDirectory = Path.Combine(Path.GetTempPath(), $"efdoctor-analyzers-{Guid.NewGuid():N}");
        var packageDirectory = Path.Combine(workDirectory, "packages");
        var projectDirectory = Path.Combine(workDirectory, "App");
        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(projectDirectory);

        try
        {
            // Pack the analyzers that the test run already built, in the same configuration.
            var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                ? "Release"
                : "Debug";
            var pack = await RunAsync(
                root,
                "pack", Path.Combine(root, "src", "EFDoctor.Analyzers", "EFDoctor.Analyzers.csproj"),
                "--no-build", "--configuration", configuration, "--output", packageDirectory);
            Assert.True(pack.ExitCode == 0, pack.StandardOutput + pack.StandardError);

            var package = Assert.Single(Directory.GetFiles(packageDirectory, "*.nupkg"));
            AssertPackageContents(package);

            // The packed folder is the only source of EFDoctor.Analyzers. EF Core comes from the
            // machine's package cache, used as a second local source, so nothing is downloaded,
            // and an isolated cache never reuses a stale copy of the same analyzer version.
            await File.WriteAllTextAsync(Path.Combine(workDirectory, "nuget.config"), $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="{Path.Combine(workDirectory, "cache")}" />
                  </config>
                  <packageSources>
                    <clear />
                    <add key="local" value="{packageDirectory}" />
                    <add key="machine" value="{MachinePackagesFolder()}" />
                  </packageSources>
                </configuration>
                """);
            await File.WriteAllTextAsync(Path.Combine(workDirectory, "Directory.Build.props"), """
                <Project>
                  <ItemGroup>
                    <PackageReference Include="EFDoctor.Analyzers" PrivateAssets="all" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(workDirectory, "Directory.Packages.props"), $"""
                <Project>
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageVersion Include="EFDoctor.Analyzers" Version="{ToolInfo.Version}" />
                    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="{RepositoryPackageVersion("Microsoft.EntityFrameworkCore")}" />
                  </ItemGroup>
                </Project>
                """);
            var project = Path.Combine(projectDirectory, "App.csproj");
            await File.WriteAllTextAsync(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ErrorLog>$(MSBuildProjectDirectory)/build.sarif,version=2.1</ErrorLog>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.EntityFrameworkCore" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Subject.cs"), Subject);

            // High confidence is a warning; medium confidence is a note that the build output
            // doesn't show.
            var production = await BuildAsync(project);
            Assert.Contains("warning EFD001", production.Output);
            Assert.DoesNotContain("EFD009", production.Output);
            Assert.Equal([("EFD001", "warning"), ("EFD009", "note")], production.Findings);
            Assert.Equal("https://github.com/vuglll/EFDoctor/blob/main/docs/rules/EFD001.md", production.HelpLinks["EFD001"]);

            var test = await BuildAsync(project, "--no-restore", "-p:IsTestProject=true");
            Assert.Empty(test.Findings);

            var optedIn = await BuildAsync(project, "--no-restore", "-p:IsTestProject=true", "-p:EFDoctorAnalyzeTestProjects=true");
            Assert.Equal([("EFD001", "warning"), ("EFD009", "note")], optedIn.Findings);
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

        foreach (var required in new[]
        {
            "analyzers/dotnet/cs/EFDoctor.Analyzers.dll",
            "build/EFDoctor.Analyzers.props",
            "PACKAGE.md",
            "icon.png",
            "NOTICE",
            "CHANGELOG.md",
        })
        {
            Assert.Contains(required, entries);
        }

        Assert.DoesNotContain(entries, static entry => entry.StartsWith("lib/", StringComparison.Ordinal));

        using var nuspecStream = archive.GetEntry("EFDoctor.Analyzers.nuspec")!.Open();
        var nuspec = XDocument.Load(nuspecStream);
        var metadata = nuspec.Root!.Elements().Single(static element => element.Name.LocalName == "metadata");
        string? Value(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value;

        Assert.Equal("EFDoctor.Analyzers", Value("id"));
        Assert.Equal(ToolInfo.Version, Value("version"));
        Assert.Equal("Apache-2.0", Value("license"));
        Assert.Equal("icon.png", Value("icon"));
        Assert.Equal("PACKAGE.md", Value("readme"));
        Assert.Equal("true", Value("developmentDependency"));
        Assert.DoesNotContain(metadata.Descendants(), static element => element.Name.LocalName == "dependency");

        // URLs appear only when Directory.Build.props sets EFDoctorRepositoryUrl.
        var configuredUrl = XDocument.Load(Path.Combine(RepositoryRoot(), "Directory.Build.props"))
            .Descendants()
            .SingleOrDefault(static element => element.Name.LocalName == "EFDoctorRepositoryUrl")
            ?.Value;
        Assert.Equal(string.IsNullOrWhiteSpace(configuredUrl) ? null : configuredUrl.Trim(), Value("projectUrl"));
    }

    private static async Task<BuildResult> BuildAsync(string project, params string[] arguments)
    {
        var directory = Path.GetDirectoryName(project)!;
        var log = Path.Combine(directory, "build.sarif");
        File.Delete(log);

        var build = await RunAsync(directory, ["build", project, .. arguments]);
        var output = build.StandardOutput + build.StandardError;
        Assert.True(build.ExitCode == 0, output);

        using var sarif = JsonDocument.Parse(await File.ReadAllTextAsync(log));
        var run = sarif.RootElement.GetProperty("runs")[0];
        var findings = run.GetProperty("results").EnumerateArray()
            .Select(static result => (Rule: result.GetProperty("ruleId").GetString()!, Level: result.GetProperty("level").GetString()!))
            .Where(static result => result.Rule.StartsWith("EFD", StringComparison.Ordinal))
            .OrderBy(static result => result.Rule, StringComparer.Ordinal)
            .ToList();
        var helpLinks = run.GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray()
            .Where(static rule => rule.TryGetProperty("helpUri", out _)
                && rule.GetProperty("id").GetString()!.StartsWith("EFD", StringComparison.Ordinal))
            .ToDictionary(static rule => rule.GetProperty("id").GetString()!, static rule => rule.GetProperty("helpUri").GetString()!);
        return new BuildResult(output, findings, helpLinks);
    }

    private static string MachinePackagesFolder()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            : configured;
    }

    private static string RepositoryPackageVersion(string packageId) =>
        XDocument.Load(Path.Combine(RepositoryRoot(), "Directory.Packages.props"))
            .Descendants()
            .Single(element => element.Name.LocalName == "PackageVersion" && element.Attribute("Include")?.Value == packageId)
            .Attribute("Version")!.Value;

    private static async Task<ProcessResult> RunAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
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
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start 'dotnet'.");
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

    private sealed record BuildResult(string Output, List<(string Rule, string Level)> Findings, Dictionary<string, string> HelpLinks);
}
