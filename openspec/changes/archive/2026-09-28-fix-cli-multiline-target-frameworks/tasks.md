# Tasks

## 1. Implementation

- [x] 1.1 Add `src/EFDoctor.Cli/build/EFDoctor.Workspace.targets`, which normalizes `TargetFrameworks` and chains to the default custom-after import. Copy it to the output directory and include it in the tool package.
- [x] 1.2 In `WorkspaceAnalyzer`, pass `CustomAfterMicrosoftCommonCrossTargetingTargets` to `MSBuildWorkspace.Create` when the file exists.

## 2. Tests (traced to `cli-workspace-loading`)

- [x] 2.1 Add an end-to-end test for a temporary restored EF project with a multi-line single-entry `TargetFrameworks`. It reports its EFD001 finding and writes nothing to stderr.
- [x] 2.2 Add an end-to-end test for a multi-line two-entry list with a trailing `;`. It reports the finding once.
- [x] 2.3 Assert that the packed tool contains `build/EFDoctor.Workspace.targets`, extending the existing package-contents test.

## 3. Documentation and validation

- [x] 3.1 Update the CHANGELOG (Unreleased) and add a README note on analyzed-target loading.
- [x] 3.2 Re-run the corpus for OpenIddict, then update `validation/FINDINGS.md` and regenerate `SUMMARY.md`.

## 4. Verification

- [x] 4.1 Build with no warnings and run the full suite.
- [x] 4.2 Run `openspec validate fix-cli-multiline-target-frameworks --strict`.
