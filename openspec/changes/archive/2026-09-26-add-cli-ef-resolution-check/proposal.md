# Proposal

## Why

Every EFDoctor rule needs EF Core types, starting with `Microsoft.EntityFrameworkCore.DbContext`, to prove anything. When a project's compilation can't resolve them, all rules stay silent. The CLI then reports **"No findings found." with exit code 0**, which reads as a clean bill of health.

Validation hit this three times (`validation/FINDINGS.md`, "Product gap"):

- **eShop:** the solution restore aborted on a MAUI workload, so projects were left unrestored.
- **OpenIddict's sandbox apps:** the same situation, projects left unrestored.
- **OpenIddict's EF Core store:** this project *was* restored. MSBuildWorkspace couldn't parse its multi-line `TargetFrameworks` property, so the design-time build failed and the compilation had no references at all.

A user who forgets to restore, or whose design-time build fails, gets a misleading clean result. CI would pass it.

## What Changes

- The CLI checks each analyzed (non-skipped) C# project. If the project's source uses EF Core, meaning it has a `using` directive for `Microsoft.EntityFrameworkCore` or one of its sub-namespaces, but its compilation can't resolve `Microsoft.EntityFrameworkCore.DbContext`, the project is **unanalyzable**.
- For each unanalyzable project, the CLI writes a warning to standard error. The warning names the project, says why its findings are missing, suggests restoring and building it, and includes up to three of that project's workspace load diagnostics.
- If at least one project uses EF Core and **none** of the EF-using projects is analyzable, the run is an analysis failure: exit code `2`, reported like any other analysis failure (console error, or the JSON error envelope). If some EF-using projects are analyzable, the run keeps its normal exit code (`0`/`1`) and prints the warnings.
- Projects with no EF Core usage are never flagged. Skipped test projects are not checked.
- The JSON report schema is unchanged. Warnings go to standard error, so standard output stays a pure report.
- The validation harness counts a run with such warnings, or with exit code `2`, as incomplete.

## Capabilities

### New Capabilities

- `cli-ef-resolution-check`: detecting and reporting projects that use EF Core but whose EF Core types can't be resolved, and the resulting warnings and exit codes.

### Modified Capabilities

None. The existing `cli-analysis` exit-code contract already defines `2` as "the target cannot be analyzed successfully". This capability says when that applies to EF resolution.

## Impact

- **Code:**
  - `WorkspaceAnalyzer`: per-project check, and it collects warnings.
  - `AnalysisResult`: gains warnings.
  - `CliApplication`: writes warnings to standard error.
  - The help text.
- **Tests:** CLI end-to-end tests with temporary unrestored EF projects, traced to the new capability.
- **Docs:** README CLI and exit-code sections, `PACKAGE.md`, and the CHANGELOG.
- **Validation:** `validation/run_corpus.py` treats warnings or exit `2` as an incomplete run. Re-run OpenIddict to confirm it is now flagged instead of reported clean.
