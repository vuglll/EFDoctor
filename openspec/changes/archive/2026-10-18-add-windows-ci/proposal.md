# Proposal

## Why

EFDoctor runs on developer machines and build agents, and many of them are Windows: the CLI loads projects through MSBuild, and the analyzer package runs inside Visual Studio and `dotnet build`. CI runs only on Ubuntu, so Windows-specific failures go unnoticed until a user reports them: path separators in reports, line endings in fixtures, and MSBuild workspace loading. The roadmap lists "Add Windows to continuous integration" as cross-cutting work.

## What Changes

- The CI workflow gains a `build-and-test-windows` job. It restores, builds with warnings as errors, and runs the full test suite with the .NET 10 SDK on `windows-latest`, beside the existing Ubuntu job.
- The existing `build-and-test` job keeps its name, so the required status check on `dev` and `main` is unchanged. Whether to make the Windows job required is a repository setting, left to the maintainer.
- Any test or product fix the Windows run needs is part of this change.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `public-release`: the CI requirement covers Linux and Windows.

## Impact

- **Code:** `.github/workflows/ci.yml`, plus any fixes that the Windows run turns up.
- **Docs:** the roadmap's cross-cutting list, `docs/development.md`, and `CONTRIBUTING.md`, if it describes CI.
