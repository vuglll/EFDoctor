# Tasks

## 1. Analyzer

- [ ] 1.1 Add `ComposedStoredProcedureAnalyzer` (EFD031): classify composing operators, walk the source through pass-through methods and locals, and read the SQL's leading keyword from a constant or interpolated argument. Verify that it builds with no warnings.

## 2. Tests and CLI

- [ ] 2.1 Add `ComposedStoredProcedureAnalyzerTests`, with at least ten positive and ten negative cases, traced, and verify that every negative compiles. Verify that the focused suite passes.
- [ ] 2.2 Register the analyzer, and add `tests/Fixtures/EFD031.Sample`, end-to-end tests, and an `EFD005.TestSample` case. Verify that the end-to-end and consistency tests pass.

## 3. Docs and validation

- [ ] 3.1 Add `docs/rules/EFD031.md`, and update the rules README, README, `PACKAGE.md`, usage, suppression, roadmap, development history, and CHANGELOG. Verify that `RepositoryConsistencyTests` pass.
- [ ] 3.2 Re-run the corpus, triage every EFD031 finding, and record the results. Verify that there is no `FP`.
- [ ] 3.3 Run `openspec validate --strict` and the denylist grep, and verify that both are clean.
