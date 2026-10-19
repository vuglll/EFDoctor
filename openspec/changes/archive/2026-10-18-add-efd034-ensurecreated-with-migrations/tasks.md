# Tasks

## 1. Analyzer

- [x] 1.1 Add `EnsureCreatedWithMigrationsAnalyzer` (EFD034), with the symbol action for migration types, operation collection of `Migrate` and `EnsureCreated` calls, and compilation-end reporting. Verify that it builds with no warnings, including the RS1037 `CompilationEnd` tag.

## 2. Tests and CLI

- [x] 2.1 Add `EnsureCreatedWithMigrationsAnalyzerTests`, with at least ten positive and ten negative cases, traced, and verify that every negative compiles. Verify that the focused suite passes.
- [x] 2.2 Register the analyzer, and add `tests/Fixtures/EFD034.Sample`, end-to-end tests, and an `EFD005.TestSample` case. Verify that the end-to-end and consistency tests pass.

## 3. Docs and validation

- [x] 3.1 Add `docs/rules/EFD034.md`, and update the rules README, README, `PACKAGE.md`, usage, suppression, roadmap, development history, and CHANGELOG. Verify that `RepositoryConsistencyTests` pass.
- [x] 3.2 Re-run the corpus, triage every EFD034 finding, and record the results. Verify that there is no `FP`.
- [x] 3.3 Run `openspec validate --strict` and the denylist grep, and verify that both are clean.
