# Usage

## Analyze a solution or project

Restore or build the target first; EFDoctor never runs `restore`. Then:

```bash
efdoctor analyze path/to/App.sln
efdoctor analyze path/to/App.csproj --format json --quiet
```

Command shape:

```text
efdoctor analyze <solution-or-project-path> [--format console|json] [--quiet] [--no-color] [--include-test-projects]
```

`efdoctor --version` prints the tool version, and `efdoctor --help` prints usage, options, and exit codes.

- Console output is the default. It includes the rule, severity, confidence, source range, evidence, likely impact, remediation, and documentation key.
- `--format json` reserves standard output for JSON, and implies no color.
- `--quiet` suppresses nonessential progress or decoration, but not the requested report or errors.
- `--no-color` removes ANSI color sequences, for automation.
- `--include-test-projects` analyzes test projects too. By default they are skipped; when included, their findings are reported at Advisory confidence and `Info` severity.
- Findings are ordered by normalized source path, start line and column, end line and column, and rule ID.

### Exit codes

| Code | Meaning |
|---:|---|
| `0` | Analysis completed successfully with no findings. |
| `1` | Analysis completed successfully with one or more findings. |
| `2` | The input was invalid, or analysis could not complete. |

Exit code `1` is a successful scan result, not an EFDoctor failure.

### JSON schema version 1

JSON reports contain a numeric `schemaVersion: 1`, a `summary.findingCount`, and an ordered `findings` array. Every finding includes `ruleId`, `ruleTitle`, `severity`, `confidence`, `message`, `sourceFile`, one-based `range` coordinates, `evidence`, `likelyImpact`, `suggestedRemediation`, and `documentationReference`.

Invalid-input and analysis failures in JSON mode use the same schema version, with an `error` object that has stable `code` and `message` fields.

### Projects whose EF Core types don't resolve

Every rule needs EF Core types. A project whose source uses EF Core (a `using Microsoft.EntityFrameworkCore…` directive), but whose compilation can't resolve `DbContext`, would otherwise look clean. That happens when the project isn't restored, or when its design-time build fails. EFDoctor doesn't analyze such a project. Instead:

- It writes `EFDoctor warning: …` to standard error, naming the project and including up to three of its load diagnostics. This happens in console and JSON modes alike, and even with `--quiet`. Standard output and the JSON schema are unchanged.
- If no project that uses EF Core could be analyzed, the run fails with exit code `2` instead of reporting no findings.

### Loading multi-targeted projects

Roslyn's MSBuildWorkspace splits a project's `TargetFrameworks` list on `;` without trimming. So a list written across several lines fails its design-time build, even though `dotnet build` accepts it.

EFDoctor works around this without touching the analyzed repository. It loads targets with the MSBuild global property `CustomAfterMicrosoftCommonCrossTargetingTargets` pointing to `build/EFDoctor.Workspace.targets`, which ships with the tool. That file:
- removes whitespace and empty entries from `TargetFrameworks` in a multi-targeted project's outer evaluation;
- then imports the SDK's default file for that hook, if one exists.

Single-targeted projects never import it. A project that sets `CustomAfterMicrosoftCommonCrossTargetingTargets` itself has its value replaced during analysis.

## Install

EFDoctor is published on nuget.org as the `EFDoctor` .NET tool, with the command `efdoctor`. Install it globally, so `efdoctor` works from any directory:

```bash
dotnet tool install --global EFDoctor
efdoctor --version
```

Global tools are installed in `~/.dotnet/tools` (`%USERPROFILE%\.dotnet\tools` on Windows). If `efdoctor` isn't found, add that directory to your `PATH`.

Or install it as a local tool, pinned in a repository's tool manifest (`dotnet-tools.json`):

```bash
dotnet new tool-manifest
dotnet tool install EFDoctor
dotnet efdoctor analyze App.sln
```

Update or remove it:

```bash
dotnet tool update --global EFDoctor
dotnet tool uninstall --global EFDoctor
```

### Prerequisites

- The .NET 10 runtime or later.
- A .NET SDK that can build the analyzed project. The SDK is resolved from the target's own directory, so a `global.json` in the target repository is honored no matter where you run `efdoctor`. If no installed SDK satisfies it, EFDoctor exits with code `2` and names the SDK version to install. In JSON mode, standard output then holds only the error envelope.
- A target that has already been restored or built.
- Only analyze repositories you trust, because loading a project runs its MSBuild logic (see [Trust boundary](#trust-boundary)).

### Build and install from source

To try unreleased changes, pack the tool from a checkout and install it from the output folder:

```bash
dotnet pack src/EFDoctor.Cli/EFDoctor.Cli.csproj -c Release -o artifacts
# creates artifacts/EFDoctor.<version>.nupkg and a matching .snupkg symbols package
dotnet tool update --global EFDoctor --add-source ./artifacts
```

NuGet caches each package version, so a rebuilt package with an unchanged `<Version>` is not picked up by `install` or `update`. Bump the version in `src/EFDoctor.Cli/EFDoctor.Cli.csproj`, or delete `~/.nuget/packages/efdoctor/<version>`, before reinstalling.

### Package contents

The package declares the Apache-2.0 license. It includes `NOTICE`, [`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md) for the bundled Roslyn and runtime libraries, and [`CHANGELOG.md`](../CHANGELOG.md). It carries repository and project URLs, and Source Link, only when the build property `EFDoctorRepositoryUrl` is set.

## Trust boundary

EFDoctor includes no telemetry, HTTP client, update check, license validation, source upload, or hosted component. Once the target and its dependencies are available locally, the CLI doesn't invoke restore and doesn't initiate network calls.

Project loading uses Roslyn's MSBuild workspace. MSBuild can execute design-time targets authored by the project being analyzed, so only analyze repositories you trust. Local-first operation keeps EFDoctor from transmitting source or findings, but it isn't a sandbox for untrusted MSBuild logic. Restore or build the target yourself before analysis, so its SDKs and dependencies are already present.
