# Proposal

## Why

Test-project detection treats any referenced assembly whose name starts with `xunit` as a test framework. Production libraries sometimes reference `xunit.assert` for guard helpers, and NuGet references flow to every project that references them. In the round-2 validation corpus, Kavita's `Kavita.Common` references `xunit.assert`, so every project that depends on it, including the EF Core data project, was classified as a test project and skipped: the scan reported no findings at all, without saying why.

## What Changes

- A project is a test project when it references a test framework's **core** assembly: `xunit`, `xunit.core`, `xunit.v3.core*`, `nunit.framework`, `Microsoft.VisualStudio.TestPlatform*`, or `Microsoft.NET.Test.Sdk`, or when `IsTestProject` is `true`.
- An assertion or abstraction library that ships alongside a framework, such as `xunit.assert` or `xunit.abstractions`, no longer makes a project a test project.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analyzer-package`: the test-framework list in "Skip test projects in builds", plus an assertion-library scenario.
- `cli-analysis`: the same list in "Downgrade findings from test projects".

## Impact

- **Code:** `EfTestProjectDetection`, shared by the analyzers and the CLI.
- **Tests:** detection tests for `xunit.assert`, `xunit.abstractions`, and `xunit.core`.
- **Docs:** the CHANGELOG.
