# Tasks

## 1. Detection

- [x] 1.1 In `WorkspaceAnalyzer`, for each analyzed project, determine whether it uses EF Core (a `Microsoft.EntityFrameworkCore` using directive) and whether `DbContext` resolves. Collect a warning for each unanalyzable project, including up to three matching load diagnostics.
- [x] 1.2 Return an analysis failure when EF-using projects exist but none is analyzable. Otherwise return success with warnings.
- [x] 1.3 Add `Warnings` to `AnalysisResult`. Have `CliApplication` write the warnings to standard error in both formats.
- [x] 1.4 Update the help text to mention the check.

## 2. Tests (traced to `cli-ef-resolution-check`)

- [x] 2.1 Test an unrestored EF-only project. Verify that it exits with `2`, names the project, and suggests restoring, in both console and JSON (error envelope) modes.
- [x] 2.2 Test a solution that contains `EFD001.Sample` and an unrestored EF project. Verify that it exits with `1`, reports the EFD001 findings, writes a warning naming the unrestored project to stderr, and that JSON stdout still parses.
- [x] 2.3 Test an unrestored project with no EF Core usage. Verify that there is no warning.
- [x] 2.4 Tag an existing resolved-fixture test (empty stderr) and the skipped-test-project test for the "no warning" scenarios.

## 3. Documentation and harness

- [x] 3.1 Update the README CLI and exit-code sections, `PACKAGE.md`, and the CHANGELOG (Unreleased).
- [x] 3.2 Make `validation/run_corpus.py` mark a run incomplete when stderr has an `EFDoctor warning`, or when the exit code is `2`, and record the warnings.

## 4. Verification

- [x] 4.1 Build with no warnings and run the full suite.
- [x] 4.2 Re-run the corpus. Confirm that OpenIddict is now reported as not analyzable instead of clean, and that the other projects are unchanged. Update `validation/FINDINGS.md` and regenerate `SUMMARY.md`.
- [x] 4.3 Run `openspec validate add-cli-ef-resolution-check --strict`.
