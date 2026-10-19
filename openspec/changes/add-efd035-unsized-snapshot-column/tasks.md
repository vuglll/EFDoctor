# Tasks

## 1. Analyzer

- [ ] 1.1 Make EFD003's `TryGetContainingEntity` internal, and add `UnsizedSnapshotColumnAnalyzer` (EFD035): read property chains, check indexed `nvarchar(max)` against snapshot coverage, check default decimal precision with the attribute exclusion, and report at two confidence levels. Verify that it builds with no warnings, and that EFD003's tests pass.

## 2. Tests and CLI

- [ ] 2.1 Add `UnsizedSnapshotColumnAnalyzerTests`, with at least ten positive and ten negative cases, traced, and verify that every negative compiles. Verify that the focused suite passes.
- [ ] 2.2 Register the analyzer, and add `tests/Fixtures/EFD035.Sample` with a snapshot, and end-to-end tests. Verify that the end-to-end and consistency tests pass.

## 3. Docs and validation

- [ ] 3.1 Add `docs/rules/EFD035.md`, and update the rules README, README, `PACKAGE.md`, usage, suppression, roadmap, development history, and CHANGELOG. Verify that `RepositoryConsistencyTests` pass.
- [ ] 3.2 Re-run the corpus, triage every EFD035 finding, and record the results. Verify that there is no `FP`.
- [ ] 3.3 Run `openspec validate --strict` and the denylist grep, and verify that both are clean.
