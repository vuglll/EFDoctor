# Proposal

## Why

EFDoctor analyzes test projects by default and downgrades their findings to advisory `Info`. In practice users run EFDoctor to find performance and safety issues in the code that ships to production; test-project findings are almost always noise, and even downgraded to `Info` they clutter the report and inflate scan time. The default should favor the common case — scan production code — and let users opt into test projects when they want them.

## What Changes

- **BREAKING (default behavior):** EFDoctor SHALL NOT analyze projects classified as test projects by default. Skipped test projects produce no findings and do not affect the exit code.
- Add a CLI flag `--include-test-projects` that opts test projects back into analysis. When set, test projects are analyzed and their findings are downgraded to advisory confidence and `Info` severity — the current behavior.
- Test-project classification is unchanged (a recognized test-framework reference or the MSBuild `IsTestProject` property).
- A target that contains only test projects (all skipped) is a successful clean scan with no findings, not an analysis failure.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `cli-analysis`: Test projects are skipped by default instead of analyzed-and-downgraded; adds the `--include-test-projects` opt-in, under which the existing downgrade behavior applies.

## Impact

- `src/EFDoctor.Cli/CliOptions.cs`: add `IncludeTestProjects` and parse `--include-test-projects`.
- `src/EFDoctor.Cli/WorkspaceAnalyzer.cs`: skip test projects unless included; when included, keep the advisory downgrade; treat an all-test-projects target as a clean success rather than a no-compilation failure.
- `src/EFDoctor.Cli/CliApplication.cs`: pass the option through; `src/EFDoctor.Cli/ToolInfo.cs`: document the flag in usage help.
- `tests/EFDoctor.Cli.Tests/`: update `TestProjectFindingsFromMultipleRulesAreDowngradedButRemainReported` (default now yields no findings; the downgrade case runs under `--include-test-projects`) and add option-parsing coverage for the new flag.
- `README.md`, `docs/rules`/`PACKAGE.md` as needed: document the default-skip behavior and the opt-in flag. No JSON schema, exit-code catalogue, network, or telemetry change.
