# Tasks

## 1. Analyzer

- [x] 1.1 Add `CancellationTokenNotPassedAnalyzer` (EFD016): recognize EF Core async operations, detect an implicitly defaulted token or a token-less `FindAsync`, and find a token parameter in scope. Verify that it builds with no warnings.

## 2. Tests and CLI

- [x] 2.1 Add `CancellationTokenNotPassedAnalyzerTests`, with at least ten positive and ten negative cases, traced, and verify that every negative compiles. Verify that the focused suite passes.
- [x] 2.2 Register the analyzer, and add `tests/Fixtures/EFD016.Sample`, end-to-end tests, and an `EFD005.TestSample` case. Verify that the end-to-end and consistency tests pass.

## 3. Docs and validation

- [x] 3.1 Add `docs/rules/EFD016.md`, and update every rule list and table, the roadmap, the development history, and the CHANGELOG. Verify that `RepositoryConsistencyTests` pass.
- [x] 3.2 Re-run the corpus, triage every EFD016 finding, and record detection precision. Verify that there is no `FP`.
- [x] 3.3 Run `openspec validate --strict` and the denylist grep, and verify that both are clean.
