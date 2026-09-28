using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Build.Locator;

namespace EFDoctor.Cli;

public static class MsBuildRegistration
{
    private const string SdkNotFoundMarker = "A compatible .NET SDK was not found";

    // Registers the .NET SDK that governs the analysis target. Resolving from the target's
    // directory (rather than the process working directory) keeps the host independent of
    // wherever the tool is launched and honors a global.json that applies to the target.
    public static bool TryRegisterFor(string targetDirectory, out string? error)
    {
        if (MSBuildLocator.IsRegistered)
        {
            error = null;
            return true;
        }

        if (PrecheckSdk(targetDirectory) is { } precheckError)
        {
            error = precheckError;
            return false;
        }

        if (!TryResolveSdk(targetDirectory, out var instance, out error))
        {
            return false;
        }

        MSBuildLocator.RegisterInstance(instance);
        return true;
    }

    public static bool TryResolveSdk(string targetDirectory, out VisualStudioInstance instance, out string? error)
    {
        instance = null!;
        List<VisualStudioInstance> instances;
        try
        {
            instances = MSBuildLocator.QueryVisualStudioInstances(new VisualStudioInstanceQueryOptions
            {
                DiscoveryTypes = DiscoveryType.DotNetSdk,
                WorkingDirectory = targetDirectory,
            }).ToList();
        }
        catch (InvalidOperationException exception) when (IsSdkResolutionFailure(exception.Message))
        {
            error = DescribeSdkResolutionFailure(targetDirectory, exception.Message);
            return false;
        }

        if (instances.Count == 0)
        {
            error = $"No installed .NET SDK could be resolved for '{targetDirectory}'. Install a .NET SDK that can build the target and try again.";
            return false;
        }

        instance = instances[0];
        error = null;
        return true;
    }

    // When hostfxr can't satisfy a global.json, it prints the installed-SDK list straight to this
    // process's standard output, which would corrupt a JSON report. MSBuildLocator can't stop that,
    // so a target governed by a global.json is first checked by running `dotnet --version` in its
    // directory with the output captured: the muxer resolves SDKs exactly as hostfxr does. Returns
    // an error only for a definite SDK-resolution failure; anything else falls through to the
    // in-process resolution.
    public static string? PrecheckSdk(string targetDirectory)
    {
        if (FindGlobalJson(targetDirectory) is null || FindDotnetHost() is not { } host)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(host, "--version")
        {
            WorkingDirectory = targetDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(PrecheckTimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            var message = standardError.GetAwaiter().GetResult() + Environment.NewLine + standardOutput.GetAwaiter().GetResult();
            return process.ExitCode != 0 && IsSdkResolutionFailure(message)
                ? DescribeSdkResolutionFailure(targetDirectory, message)
                : null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private const int PrecheckTimeoutMilliseconds = 30_000;

    private static string? FindGlobalJson(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "global.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // Mirrors how MSBuildLocator finds the host, so both see the same set of SDKs.
    private static string? FindDotnetHost()
    {
        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath && File.Exists(hostPath))
        {
            return hostPath;
        }

        if (Environment.ProcessPath is { } processPath
            && string.Equals(Path.GetFileName(processPath), executable, StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root
            && Path.Combine(root, executable) is var rootHost
            && File.Exists(rootHost))
        {
            return rootHost;
        }

        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executable))
            .FirstOrDefault(File.Exists);
    }

    public static bool IsSdkResolutionFailure(string message) =>
        message.Contains(SdkNotFoundMarker, StringComparison.Ordinal);

    public static string DescribeSdkResolutionFailure(string targetDirectory, string message)
    {
        var requested = Match(message, @"Requested SDK version:\s*(?<value>\S+)");
        var globalJson = Match(message, @"global\.json file:\s*(?<value>.+?)\s*$");
        var details = requested is null
            ? string.Empty
            : globalJson is null
                ? $" (requested SDK {requested})"
                : $" (requested SDK {requested} in {globalJson})";
        return $"No installed .NET SDK satisfies the global.json that applies to '{targetDirectory}'{details}. Install that SDK or update the global.json to match an installed SDK.";
    }

    private static string? Match(string message, string pattern)
    {
        var match = Regex.Match(message, pattern, RegexOptions.Multiline);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }
}
