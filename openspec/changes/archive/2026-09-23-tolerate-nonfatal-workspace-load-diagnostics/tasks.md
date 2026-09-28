# Tasks

## 1. Refactor workspace failure handling

- [x] 1.1 In `WorkspaceAnalyzer.AnalyzeAsync`, remove the early `if (!failures.IsEmpty) return Failure(...)` gate while still collecting `WorkspaceDiagnosticKind.Failure` messages. Verify the file compiles.
- [x] 1.2 Iterate C# projects, obtain each compilation, skip (do not abort on) a project whose compilation is null or has no syntax trees, and track whether any compilation was analyzed. Verify with the existing passing fixtures that normal targets still report identical findings. (A failed design-time build yields a non-null but empty compilation, so the "has syntax trees" check — not just null — is what distinguishes an analyzable project.)
- [x] 1.3 Return `AnalysisResult.Failure` only when no C# compilation was analyzed: use the "MSBuild could not load the target: …" message (with the collected diagnostics) when the workspace queue is non-empty, otherwise a generic "Could not create a C# compilation for the target." message. Verify `ExistingButBrokenProjectReturnsAnalysisFailure` still returns exit code 2 with the expected message.

## 2. Regression coverage

- [x] 2.1 Add coverage for a target that loads with a workspace failure but still yields an analyzable compilation. Implemented as an end-to-end solution fixture pairing a restored good fixture (`EFD001.Sample`) with a broken sibling project (missing import) synthesized in a temp directory, so no network is required.
- [x] 2.2 Add an end-to-end CLI test (`SolutionWithOneUnloadableProjectStillAnalyzesTheRest`) asserting the good project's findings are reported with the successful-scan-with-findings exit code (`1`) and no "MSBuild could not load" error. Verified passing.
- [x] 2.3 Confirm the genuinely-broken-project test (`ExistingButBrokenProjectReturnsAnalysisFailure`) and the clean-scan tests still pass unchanged. Verified.

## 3. Verification and finalization

- [x] 3.1 Build the solution from a clean state with the documented .NET command and verify no errors or warnings. (`dotnet build EFDoctor.sln`: 0 warnings, 0 errors, with `TreatWarningsAsErrors=true`.)
- [x] 3.2 Run the full automated suite and verify all analyzer and CLI end-to-end tests pass, including the new test. (579 analyzer + 41 CLI = 620 passed.)
- [ ] 3.3 Manually run the CLI against a real project that emits build warnings and confirm it now reports findings with a stable exit code instead of the "MSBuild could not load the target" failure. (Covered in principle by the partial-solution E2E test; end-user confirmation on their originating project is pending.)
- [x] 3.4 Sync the `cli-analysis` delta to the main spec and run strict OpenSpec validation for the change.
