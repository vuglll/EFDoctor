# Proposal

## Why

EFDoctor currently runs only from a repository checkout through `dotnet run`, so every tester must clone, restore, and build it before trying it on their own code. The product brief chose a `dotnet` global tool distributed through NuGet as the v1 form factor, and the next validation step (real-project precision testing with outside users) needs an installable preview. Packaging also exposes two gaps that matter once the tool runs outside the repository: it registers an MSBuild SDK before it knows the target, so a target's `global.json` is ignored, and it has no `--version` or `--help` output.

## What Changes

- Package the CLI as a .NET tool with command name `efdoctor` and package ID `EFDoctor`, starting at version `0.1.0-preview.1`, runnable on .NET 10 or later runtimes.
- Add NuGet package metadata suitable for nuget.org while the repository stays private: description, tags, icon, a package readme, release notes, the Apache-2.0 license expression, the `NOTICE` file, third-party notices for bundled dependencies, and a symbols package. Omit repository and project URLs.
- Select the .NET SDK used to load a target from the target's own directory, so the target's `global.json` is honored, and report a clear analysis failure when no matching SDK is installed.
- Add `--version` and `--help` to the CLI.
- Record the first release in the analyzer release-tracking files and add a `CHANGELOG.md`.
- Add an automated pack → install into a temporary tool path → analyze smoke test, and document installing and running the tool in the README.
- Local build, pack, and install only. Publishing to nuget.org and a CI release workflow are out of scope.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `cli-analysis`: Adds requirements for tool packaging and installation, version and usage output, and target-relative SDK selection.

## Impact

Affects the CLI project file, shared build properties, `Program.cs`, `CliApplication`, `CliOptions`, `MsBuildRegistration`, the analyzer release-tracking files, new package assets (package readme, icon, third-party notices, changelog), CLI tests (option parsing, SDK selection, packaging smoke test), and the README. No analyzer behavior, finding contract, JSON schema, or exit-code meaning changes.
