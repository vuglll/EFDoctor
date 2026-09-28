# Design

## Context

The CLI project (`src/EFDoctor.Cli`, assembly name `efdoctor`, `net10.0`) references the analyzers (`netstandard2.0`) and core projects, `Microsoft.Build.Locator` 1.11.2, `Microsoft.Build.Framework` with runtime assets excluded, and Roslyn `Workspaces.MSBuild` 5.9.0. Its build output already contains Roslyn's out-of-process `BuildHost-netcore` and `BuildHost-net472` folders, 13 satellite-resource language folders from Roslyn, and no MSBuild runtime assemblies other than the locator.

`Program.cs` calls `MsBuildRegistration.EnsureRegistered()` (`MSBuildLocator.RegisterDefaults()`) before arguments are parsed, and `CliApplication.RunAsync` calls it again after validation. `RegisterDefaults` resolves SDKs from the process working directory. `CliOptions` accepts only `analyze <path> [options]`. Any other first argument is a usage error with exit code `2`.

Shared build properties already set `Deterministic`, `ContinuousIntegrationBuild`, `TreatWarningsAsErrors`, `Authors`, `Copyright`, and `PackageLicenseExpression` (`Apache-2.0`). `AnalyzerReleases.Shipped.md` lists EFD001 under a "Release 1.0.0" heading that was never published, and `AnalyzerReleases.Unshipped.md` lists EFD002–EFD019. The machine used for development has SDKs 6.0 through 10.0 installed.

See `proposal.md` for motivation and the `cli-analysis` spec delta for observable behavior.

## Goals / Non-Goals

**Goals:**

- Produce a nuget.org-ready tool package and symbols package from `dotnet pack`, installable from a local source.
- Honor the analyzed target's SDK selection and fail clearly when it can't be satisfied.
- Prove the packaged tool works with an automated pack → install → run test, not only the repository build.

**Non-Goals:**

- Publishing to nuget.org, API keys, author signing, or a CI release workflow.
- An analyzer-only NuGet package for IDE and build integration.
- Changing analyzer behavior, the finding contract, the JSON schema, or exit-code meanings.
- Making the repository public or adding repository links.

## Decisions

### Package metadata lives in the CLI project; shared identity stays in build props

Add `PackAsTool`, `ToolCommandName` (`efdoctor`), `PackageId` (`EFDoctor`), `Version` (`0.1.0-preview.1`), `Description`, `PackageTags`, `PackageReadmeFile`, `PackageIcon`, `PackageReleaseNotes`, `RollForward` (`Major`), `IncludeSymbols`, and `SymbolPackageFormat` (`snupkg`) to `EFDoctor.Cli.csproj`. Package assets live in `src/EFDoctor.Cli/Package/` (`PACKAGE.md`, `icon.png`). The root `NOTICE` and a new root `THIRD-PARTY-NOTICES.md` are packed at the package root. Authors, copyright, and the license expression stay in `Directory.Build.props`.

`SatelliteResourceLanguages` is set to `en` for the CLI, which drops Roslyn's localized resource folders. EFDoctor's own output is English-only, and this shrinks the package noticeably.

### No repository URLs while the repository is private

Set `PublishRepositoryUrl` to `false` and `EnableSourceLink` to `false`, and set no `RepositoryUrl` or `PackageProjectUrl`. This keeps the private repository's URL out of both the `.nuspec` and the symbol files. SourceLink can be enabled when the repository becomes public. The symbols package still gives readable stack traces locally.

### Register MSBuild from the target's directory and translate SDK-resolution failures

A probe during implementation established how SDK selection actually works. The target pinned SDK 8.0.403 through `global.json`, and SDKs 6.0–10.0 were installed:

- Roslyn's out-of-process BuildHost resolves the SDK for each project from the project's own directory. The pinned target loaded with 8.0.403 both when the host registered SDK 10 through `RegisterDefaults` from an unrelated directory and when the host registered from the target's directory. Project loading therefore already honors the target's `global.json`.
- `RegisterDefaults` resolves from the process working directory and throws `hostfxr_resolve_sdk2 ... A compatible .NET SDK was not found` when that directory's `global.json` can't be satisfied. Because `Program.cs` calls it before any error handling, running the tool from such a directory crashes with an unhandled exception, even for a valid target.
- A target whose own `global.json` can't be satisfied already fails with exit code `2`, but the message is the raw wrapped hostfxr exception.

Decision: remove the early registration from `Program.cs`. After validating the target, `CliApplication` calls `MsBuildRegistration.TryRegisterFor(targetDirectory, out error)`. It queries `MSBuildLocator.QueryVisualStudioInstances` with `DiscoveryTypes = DotNetSdk` and `WorkingDirectory = targetDirectory`, registers the first instance, and returns a descriptive error when there are no instances or the SDK resolver throws. The same message translation is applied when an SDK-resolution failure surfaces from project loading. The error names the target directory and the requested SDK version, and tells the user to install that SDK or update `global.json`. An internal `ResolveSdk(targetDirectory)` helper lets tests check selection without registering, which can happen only once per process.

This makes host behavior independent of the working directory, and leaves project loading to the BuildHost's existing per-project resolution, which the probe verified.

### Version and help are handled before command parsing

`CliOptions.TryParse` gains a small command result: `Analyze`, `Version`, or `Help`. `--version` prints the assembly's informational version without build metadata (the part after `+`), and `--help`/`-h` prints the command shape, options, and exit codes. Both write to standard output and return `0` before any MSBuild registration. No-argument and unknown-command behavior is unchanged.

### Record the first release in release tracking

Since nothing has been published, replace the unpublished "Release 1.0.0" heading with "Release 0.1.0" containing all twelve shipped rules (EFD001–EFD006, EFD011–EFD013, EFD017–EFD019), and leave `AnalyzerReleases.Unshipped.md` with only its header. Add `CHANGELOG.md` with a `0.1.0-preview.1` entry. The package's `PackageReleaseNotes` summarizes it.

### Third-party notices from the actual dependency closure

Generate `THIRD-PARTY-NOTICES.md` from the packages in the CLI's `efdoctor.deps.json`, reading each package's license from its `.nuspec` in the local NuGet cache. Every expected dependency (Roslyn, MSBuildLocator, `Microsoft.Extensions.*`, `System.Composition.*`, Humanizer, `Microsoft.VisualStudio.SolutionPersistence`) is expected to be MIT-licensed, and implementation verifies that rather than assuming it.

### Automated packaging smoke test

A CLI test packs the already built CLI (`dotnet pack --no-build`) into a temporary directory, inspects the `.nupkg`, and installs it with `dotnet tool install --tool-path` using a temporary `nuget.config` that clears all sources except the local one, so the test makes no network calls. It then runs the installed command with `--version` and with `analyze` on `EFD001.Sample --format json`, and compares the result with the repository-built CLI. The package inspection checks for the `.nuspec` license expression, the absence of repository URLs, `NOTICE`, third-party notices, the readme, the icon, `BuildHost-netcore`, and the absence of `Microsoft.Build.dll` and related MSBuild runtime assemblies.

### Icon

A 128×128 PNG generated once by a small script (a rounded teal square with a white medical cross) and committed as a binary asset. No imaging library is needed.

## Risks / Trade-offs

- [The BuildHost performs its own SDK discovery] → Verified by the probe: it honors the target's `global.json`. Host registration only needs to avoid depending on the working directory.
- [The locator filters out SDKs newer than the running runtime] → A `.NET 10` tool with `RollForward=Major` runs on newer runtimes. Older SDKs remain eligible. The failure message names the target directory so the cause is clear.
- [The packaging test is slow, since it packs and installs a real tool] → It runs once per test run and reuses the existing build output. Keep it to one test method.
- [`ContinuousIntegrationBuild` is on for local builds] → Unchanged here, because it only normalizes paths. It can be scoped to CI when a CI pipeline exists.
- [Package size, since Roslyn and the workspace libraries are large] → Accepted. Trimming satellite resources removes the easy excess.

## Migration Plan

Pack and install locally, and share the `.nupkg` with testers through a local folder or zip. Rollback consists of reverting the project metadata and CLI changes; nothing is published. When the repository becomes public, enable SourceLink and add repository URLs in a follow-up change.
