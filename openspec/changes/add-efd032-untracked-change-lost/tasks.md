# Tasks

## 1. Analyzer

- [ ] 1.1 Add `UntrackedChangeLostAnalyzer` (EFD032). It handles save invocations, the backward walk to an untracked load on the same context, foreach expansion, the reference whitelist, and the first-assignment report. Verify that it builds with no warnings.

## 2. Tests and CLI

- [ ] 2.1 Add `UntrackedChangeLostAnalyzerTests`, with at least ten positive and ten negative cases, covering every scenario and suppression, and traced. Verify that the focused suite passes.
- [ ] 2.2 Register the analyzer, and add `tests/Fixtures/EFD032.Sample`, end-to-end tests, and an `EFD005.TestSample` case. Verify that the end-to-end and consistency tests pass.

## 3. Docs and validation

- [ ] 3.1 Add `docs/rules/EFD032.md`, and update the rules README, README, `PACKAGE.md`, usage, suppression, roadmap, development history, and CHANGELOG. Verify that `RepositoryConsistencyTests` pass.
- [ ] 3.2 Re-run the corpus, triage every EFD032 finding, and record the results. Verify that there is no `FP`.
- [ ] 3.3 Run `openspec validate --strict` and the denylist grep, and verify that both are clean.
