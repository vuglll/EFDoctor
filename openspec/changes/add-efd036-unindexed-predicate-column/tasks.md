# Tasks

## 1. Analyzer

- [ ] 1.1 Make EFD003's snapshot collector types internal, and verify that EFD003's tests are unchanged.
- [ ] 1.2 Add `UnindexedPredicateColumnAnalyzer` (EFD036): snapshot coverage collection, predicate filter-column extraction, and compilation-end reporting with the `CompilationEnd` tag. Verify that it builds with no warnings.

## 2. Tests and CLI

- [ ] 2.1 Add `UnindexedPredicateColumnAnalyzerTests`, with at least ten positive and ten negative cases, traced, and verify that every negative compiles. Verify that the focused suite passes.
- [ ] 2.2 Register the analyzer, and add `tests/Fixtures/EFD036.Sample` with a snapshot, and end-to-end tests. Add an `EFD005.TestSample` case where the shared sample allows it. Verify that the end-to-end and consistency tests pass.

## 3. Docs and validation

- [ ] 3.1 Add `docs/rules/EFD036.md`, and update the rules README, README, `PACKAGE.md`, usage, suppression, roadmap, development history, and CHANGELOG. Verify that `RepositoryConsistencyTests` pass.
- [ ] 3.2 Re-run the corpus, triage every EFD036 finding, and record detection precision. Verify that there is no `FP`.
- [ ] 3.3 Run `openspec validate --strict` and the denylist grep, and verify that both are clean.
