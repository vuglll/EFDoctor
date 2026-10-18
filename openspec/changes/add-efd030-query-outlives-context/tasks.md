# Tasks

## 1. Analyzer

- [ ] 1.1 Add `QueryOutlivesContextAnalyzer` (EFD030: `Reliability`, `Warning`, high). It needs return-operation analysis, the deferred-value check, the origin proof, context resolution, the using-scope test in the same function, and DI-scope resolution. Verify that it builds with no warnings.

## 2. Tests and CLI

- [ ] 2.1 Add `QueryOutlivesContextAnalyzerTests`, with at least ten positive and ten negative cases, covering every scenario and suppression, and traced. Verify that the focused suite passes.
- [ ] 2.2 Register the analyzer in `WorkspaceAnalyzer` and the release file. Add `tests/Fixtures/EFD030.Sample`, its end-to-end tests, and an `EFD005.TestSample` case. Verify that the end-to-end and consistency tests pass.

## 3. Docs and validation

- [ ] 3.1 Add `docs/rules/EFD030.md`, and update the rules README, README, `PACKAGE.md`, usage, suppression, roadmap, development history, and CHANGELOG. Verify that `RepositoryConsistencyTests` pass.
- [ ] 3.2 Re-run the corpus and triage every EFD030 finding. Verify that there is no `FP`.
- [ ] 3.3 Run `openspec validate --strict` and the denylist grep, and verify that both are clean.
