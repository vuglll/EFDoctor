# Tasks

## 1. CLI option

- [x] 1.1 Add `IncludeTestProjects` (default `false`) to the `CliOptions` record and parse `--include-test-projects` in `CliOptions.TryParse`. Verify with a unit test that the flag sets the option and that its absence leaves it `false`.

## 2. Analyzer behavior

- [x] 2.1 Add an `includeTestProjects` parameter (default `false`) to `WorkspaceAnalyzer.AnalyzeAsync`; in the project loop, `continue` past a project when `IsTestProject(project, compilation)` and not included, before running analyzers. When included, keep the existing `DowngradeToAdvisory` mapping. Verify normal (non-test) targets are unaffected.
- [x] 2.2 Track that a test project was intentionally skipped and, when no compilation was analyzed but a test project was skipped and there is no genuine load failure, return a successful empty result rather than an analysis failure. Verify a solution containing only a test project returns exit code `0` with no findings by default.
- [x] 2.3 Pass `options.IncludeTestProjects` from `CliApplication` into `AnalyzeAsync`. Verify the flag reaches the analyzer end to end.

## 3. Usage and docs

- [x] 3.1 Document `--include-test-projects` in `ToolInfo` usage help. Verify `efdoctor --help` lists the flag and its meaning.
- [x] 3.2 Update `README.md` (and `PACKAGE.md`/rule docs where they describe test-project handling) to state that test projects are skipped by default and analyzed only with `--include-test-projects`. Verify no doc still says test findings are reported/downgraded by default.

## 4. Tests

- [x] 4.1 Update `TestProjectFindingsFromMultipleRulesAreDowngradedButRemainReported`: by default the test-project fixture yields no findings and exit code `0`; add coverage running the same fixture with `--include-test-projects` that asserts the downgraded advisory/`Info` findings and exit code `1`.
- [x] 4.2 Add/adjust an end-to-end test proving a mixed solution reports production findings by default while omitting test-project findings, and reports both (test ones downgraded) with `--include-test-projects`.
- [x] 4.3 Confirm no other analyzer or CLI test regressed by the default change.

## 5. Verification and finalization

- [x] 5.1 Build the solution from a clean state with the documented .NET command and verify no errors or warnings.
- [x] 5.2 Run the full automated suite and verify all analyzer and CLI end-to-end tests pass.
- [x] 5.3 Manually run `efdoctor analyze` on a solution with a test project, confirming it is skipped by default and included with `--include-test-projects`.
- [x] 5.4 Sync the `cli-analysis` delta to the main spec and run strict OpenSpec validation for the change.
