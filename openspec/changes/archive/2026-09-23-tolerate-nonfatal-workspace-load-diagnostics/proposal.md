# Proposal

## Why

EFDoctor aborts a scan with analysis-failure exit code `2` and the message "MSBuild could not load the target" whenever `MSBuildWorkspace` raises any `WorkspaceDiagnosticKind.Failure` during load. But `MSBuildWorkspace` raises `Failure`-kind diagnostics for many **non-fatal** conditions — a target that emits build warnings, an unresolved optional import, an analyzer or source generator that fails to load, or a partially-failing referenced project — even when a fully usable C# `Compilation` is still produced. As a result, real projects that merely have build warnings cannot be analyzed at all, which is a common and blocking situation for users pointing EFDoctor at their own solutions.

## What Changes

- EFDoctor SHALL NOT treat workspace load diagnostics as an automatic scan failure. It SHALL proceed to analyze every C# project for which a compilation is available, even when the workspace reported load diagnostics.
- EFDoctor SHALL fail the scan with the analysis-failure exit code only when the target produced **no analyzable C# compilation at all**. In that case the reported error SHALL still include the collected workspace diagnostics as the explanation.
- Genuinely unloadable targets (for example a project importing a missing `.targets` file, which yields no compilation) continue to fail with exit code `2`, unchanged.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `cli-analysis`: Refines analysis-failure semantics — a workspace load diagnostic alone no longer fails the scan; failure occurs only when no C# compilation can be produced. Adds observable behavior for targets that load with build warnings.

## Impact

- `src/EFDoctor.Cli/WorkspaceAnalyzer.cs`: stop aborting on non-empty workspace-failure queue; analyze projects that produced a compilation; fail only when none did, surfacing the collected diagnostics.
- `tests/EFDoctor.Cli.Tests/`: add an end-to-end fixture/test for a target that loads with build warnings (expect findings and success), keep the existing genuinely-broken-project failure test.
- No CLI command surface, JSON schema, exit-code catalogue, network, or telemetry change; other rules and analyzers are unaffected.
