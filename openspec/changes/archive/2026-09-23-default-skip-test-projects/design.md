# Design

## Context

See `proposal.md` for motivation. Today `WorkspaceAnalyzer.AnalyzeAsync(targetPath, cancellationToken)` analyzes every C# project and, for projects `IsTestProject(...)` recognizes as tests, maps their findings through `DowngradeToAdvisory`. `CliOptions` is a record parsed by `CliOptions.TryParse`, carrying `TargetPath`, `Format`, `Quiet`, `NoColor`. `CliApplication` calls `AnalyzeAsync` with just the path. The recent "tolerate non-fatal load diagnostics" change made the run fail only when no analyzable compilation loads.

## Goals / Non-Goals

**Goals:**

- Skip test projects by default; add `--include-test-projects` to restore analysis with the existing advisory downgrade.
- Keep test-project classification and the downgrade logic exactly as they are; only change *when* they apply.
- Do not turn an all-test-projects target into a false analysis failure.

**Non-Goals:**

- Changing project classification, the downgrade mapping, exit-code values, output formats, or the JSON schema.
- A per-project include/exclude selection language; this is a single boolean flag.

## Decisions

### 1. Single boolean flag threaded to the analyzer

Add `IncludeTestProjects` (default `false`) to the `CliOptions` record and parse `--include-test-projects` in `TryParse` alongside `--quiet`/`--no-color`. Add an `includeTestProjects` parameter (default `false`) to `WorkspaceAnalyzer.AnalyzeAsync`; `CliApplication` passes `options.IncludeTestProjects`. The default value keeps the analyzer's existing signature callable and makes "skip" the default everywhere.

Alternative considered: invert an existing flag or reuse `--quiet`. Rejected: orthogonal concerns; an explicit, discoverable flag is clearer.

### 2. Skip happens in the project loop, reusing existing classification

In the project loop, when a project is `IsTestProject(...)` and `!includeTestProjects`, `continue` before creating a compilation-with-analyzers — this also avoids the analysis cost. When `includeTestProjects` is set, analyze and apply `DowngradeToAdvisory` exactly as today.

### 3. Skipped test projects are not a load failure

The prior change fails the run when no analyzable compilation loaded. Skipping is intentional, not a failure, so track it separately: record that at least one project was intentionally skipped. When no compilation was analyzed *and* a test project was skipped (and there is no genuine load failure), return a successful empty result rather than an analysis failure. Only a target that produced nothing analyzable and skipped nothing (or failed to load) is a failure. This keeps "solution of only test projects" a clean exit-0 scan.

Alternative considered: count skipped test projects as "analyzed" to bypass the failure check. Rejected: that would also mask a genuine no-compilation case when the only project is a test project that failed to load; tracking the skip explicitly is precise.

## Risks / Trade-offs

- **[Users who relied on seeing downgraded test findings by default lose them]** → This is the intended behavior change (documented as BREAKING in the proposal); `--include-test-projects` restores the old output. README and release notes call it out.
- **[Existing tests assume downgrade-by-default]** → Update `TestProjectFindingsFromMultipleRulesAreDowngradedButRemainReported` to assert no findings by default and to exercise `--include-test-projects` for the downgrade path.

## Migration Plan

1. Add `IncludeTestProjects` to `CliOptions` and parse the flag.
2. Thread it into `AnalyzeAsync`; skip test projects unless included; keep the downgrade when included; treat all-skipped as clean success.
3. Update `CliApplication` and `ToolInfo` usage help.
4. Update the affected test(s); add flag parse coverage.
5. Update README/PACKAGE docs; sync spec and archive.

Rollback restores analyze-and-downgrade-by-default and removes the flag. No persisted data or schema change.
