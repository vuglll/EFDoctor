# Tasks

## 1. Analyzer

- [x] 1.1 Add `ClientMethodInQueryAnalyzer` with the EFD028 descriptor (`Correctness`, `Warning`, high, help link) and the shared conventions. It handles invocation operations, the lambda walk to the translated operator, the parameter-dependence check, and source proof with the origin proof. Verify that it builds with no warnings.
- [x] 1.2 Implement the exclusions: attribute names, `DbContext` static methods, throw-only bodies, and the lazy `HasDbFunction` name scan. Verify with negative tests for each.

## 2. Tests

- [x] 2.1 Add `ClientMethodInQueryAnalyzerTests`, with at least ten positive and ten negative cases. Cover every spec scenario, plus pragma, editor configuration, `SuppressMessage`, and generated code, and tag every scenario with `[Trait("Spec", ...)]`. Verify that the focused suite passes.
- [x] 2.2 Register the analyzer in `WorkspaceAnalyzer` and `AnalyzerReleases.Unshipped.md`. Add `tests/Fixtures/EFD028.Sample`, with positive, negative, and suppressed cases, under the Fixtures solution folder. Add console and JSON end-to-end tests. Add an EFD028 case to `EFD005.TestSample`, and update its expected count and order. Verify that the end-to-end and consistency tests pass.

## 3. Docs and validation

- [x] 3.1 Add `docs/rules/EFD028.md` and its rules-README boundary section. Update:
  - the README rule table and status line;
  - `PACKAGE.md`;
  - the usage severity table;
  - the suppression guide;
  - the roadmap's shipped and candidate tables;
  - the development history;
  - the CHANGELOG.
  
  Verify that `RepositoryConsistencyTests` pass.
- [x] 3.2 Re-run the corpus, triage every EFD028 finding, and record the results in `validation/FINDINGS.md`. Verify that no finding is `FP`, or fix the cause.
- [x] 3.3 Run `openspec validate --strict` and the denylist grep, and verify that both are clean.
