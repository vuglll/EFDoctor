# Design

## Context

See `proposal.md` for motivation. `WorkspaceAnalyzer.AnalyzeAsync` (`src/EFDoctor.Cli/WorkspaceAnalyzer.cs`) registers a `WorkspaceFailedHandler` that enqueues every `WorkspaceDiagnosticKind.Failure` message, opens the project/solution, and then — before touching any compilation — returns `AnalysisResult.Failure("MSBuild could not load the target: …")` if the queue is non-empty. Only after that early return does it iterate projects and call `GetCompilationAsync`.

`WorkspaceDiagnosticKind` has just two values, `Failure` and `Warning`, and `MSBuildWorkspace` emits `Failure` for many non-fatal load conditions (build warnings surfaced during design-time build, unresolved optional imports, analyzer/generator load problems, a partially failing referenced project) while still producing a usable `Compilation`. The early return therefore rejects analyzable targets.

The existing test `ExistingButBrokenProjectReturnsAnalysisFailure` creates a project importing a missing `.targets` file; that target yields no compilation, so it must still fail.

## Goals / Non-Goals

**Goals:**

- Analyze targets that load with non-fatal workspace diagnostics (including build warnings).
- Preserve a clear analysis failure — with the diagnostics as explanation — when nothing analyzable loads.
- Keep the change local to `WorkspaceAnalyzer`; no change to exit-code values, output formats, or the finding contract.

**Non-Goals:**

- Surfacing or formatting workspace warnings as findings or as a separate report section.
- Distinguishing "compiles with errors" from "compiles cleanly" — EFDoctor analyzes whatever compilation Roslyn produces, as today.
- Restoring or building the target; loading remains design-time only.

## Decisions

### 1. Availability of a compilation decides success, not the workspace-failure queue

Remove the early `if (!failures.IsEmpty) return Failure(...)` gate. Keep collecting the `Failure`-kind messages into the queue for reporting. After opening the target, iterate the C# projects and collect compilations; track whether any compilation was obtained.

- If at least one C# compilation is available, run the analyzers over each available compilation and return `AnalysisResult.Success` with the findings, ignoring the collected diagnostics for exit-code purposes.
- If no C# compilation is available (no C# projects, or every `GetCompilationAsync` returned null), return `AnalysisResult.Failure`. When the workspace queue is non-empty, the message keeps the current "MSBuild could not load the target: …" text so the broken-project test and its assertion continue to pass; when the queue is empty, fall back to the existing per-project "Could not create a C# compilation" style message.

A project whose `GetCompilationAsync` returns null no longer aborts the whole run; it is skipped, and only contributes to failure if it leaves the run with zero compilations.

Alternative considered: filter the workspace diagnostics to a curated "fatal" subset and keep the early gate. Rejected: `WorkspaceDiagnosticKind` does not distinguish fatal from non-fatal, and message-text matching is brittle across SDK versions. "Did a compilation load?" is the robust, provider- and SDK-independent signal.

Alternative considered: treat only `Warning`-kind as tolerable and keep failing on `Failure`-kind. Rejected: the reported kind is exactly `Failure` for the non-fatal cases this change targets, so it would not fix the problem.

## Risks / Trade-offs

- **[A partially-loaded target hides a real load problem behind a green scan]** → Acceptable and preferable to blocking analysis; the target still compiles enough to analyze, and genuine no-compilation cases still fail. The collected diagnostics remain available for the failure path. A future enhancement could echo non-fatal diagnostics to stderr without changing the exit code.
- **[The broken-project test depends on the exact failure message]** → Preserve the "MSBuild could not load the target: …" message on the no-compilation-with-queued-diagnostics path so the existing assertion holds.

## Migration Plan

1. Refactor `WorkspaceAnalyzer.AnalyzeAsync` per Decision 1.
2. Add an end-to-end test with a fixture that loads but emits build warnings, asserting success and expected findings.
3. Keep `ExistingButBrokenProjectReturnsAnalysisFailure` passing unchanged.
4. Sync the `cli-analysis` delta and archive.

Rollback restores the early failure gate. No persisted data or schema change.
