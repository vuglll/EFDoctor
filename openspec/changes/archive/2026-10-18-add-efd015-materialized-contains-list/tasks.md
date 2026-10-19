# Tasks

## 1. Analyzer

- [x] 1.1 Add `MaterializedContainsListAnalyzer` (EFD015): find `Contains` in SQL lambdas, follow the collection local to an unbounded materialization on the same context, and require every use of the local to be a `Contains` collection. Verify that it builds with no warnings.

## 2. Tests and CLI

- [x] 2.1 Add `MaterializedContainsListAnalyzerTests`, with at least ten positive and ten negative cases, traced, and verify that every negative compiles. Verify that the focused suite passes.
- [x] 2.2 Register the analyzer, and add `tests/Fixtures/EFD015.Sample`, end-to-end tests, and an `EFD005.TestSample` case. Verify that the end-to-end and consistency tests pass.

## 3. Docs and validation

- [x] 3.1 Add `docs/rules/EFD015.md`, and update every rule list and table, the roadmap, the development history, and the CHANGELOG. Verify that `RepositoryConsistencyTests` pass.
- [x] 3.2 Re-run the corpus, triage every EFD015 finding, and record detection precision. Verify that there is no `FP`.
- [x] 3.3 Run `openspec validate --strict` and the denylist grep, and verify that both are clean.
