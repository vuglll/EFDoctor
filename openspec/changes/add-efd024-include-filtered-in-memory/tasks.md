# Tasks

## 1. Analyzer

- [ ] 1.1 Add `IncludeFilteredInMemoryAnalyzer` (EFD024): find plain collection includes in materialized loads, find the entity variables, require every navigation use to be filtered, and check that nothing escapes. Verify that it builds with no warnings.

## 2. Tests and CLI

- [ ] 2.1 Add `IncludeFilteredInMemoryAnalyzerTests`, with at least ten positive and ten negative cases, traced, and verify that every negative compiles. Verify that the focused suite passes.
- [ ] 2.2 Register the analyzer, and add `tests/Fixtures/EFD024.Sample`, end-to-end tests, and an `EFD005.TestSample` case. Verify that the end-to-end and consistency tests pass.

## 3. Docs and validation

- [ ] 3.1 Add `docs/rules/EFD024.md`, and update every rule list and table, the roadmap, the development history, and the CHANGELOG. Verify that `RepositoryConsistencyTests` pass.
- [ ] 3.2 Re-run the corpus, triage every EFD024 finding, and record detection precision. Verify that there is no `FP`.
- [ ] 3.3 Run `openspec validate --strict` and the denylist grep, and verify that both are clean.
