# Tasks

## 1. Analyzer

- [x] 1.1 Extract `EfContextIdentity` from EFD027 and use it there; verify the EFD027 tests pass unchanged.
- [x] 1.2 Add `StaleTrackedEntitiesAnalyzer` (EFD038): tracked loads, bulk operations on both EF Core extension types, block-order proof, context identity, clear/reload/detach silence, the disjoint-constant filter check, and the two confidence tiers; follow the shared analyzer conventions.
- [x] 1.3 Add analyzer tests tagged to every scenario, with at least ten positive and ten negative fixtures; verify they pass and that removing a guard fails a negative fixture.
- [x] 1.4 Register the analyzer in `WorkspaceAnalyzer`, and add the `AnalyzerReleases.Unshipped.md` entry.

## 2. Fixtures and end-to-end

- [x] 2.1 Add `tests/Fixtures/EFD038.Sample` to the solution under the existing Fixtures folder, with high, medium, silent, and suppressed cases; add a console and JSON end-to-end test with exact ranges.
- [x] 2.2 Add a case to the shared test-project fixture, and update the expected count and rule order.

## 3. Documentation

- [x] 3.1 Add `docs/rules/EFD038.md`, the boundary section in `docs/rules/README.md`, and a reference from `docs/rules/EFD013.md`.
- [x] 3.2 Update the README rule table and status line, `PACKAGE.md`, the usage guide's severity table, the suppression guide, the development guide (verification map and implementation history), the roadmap, and the changelog.

## 4. Verification

- [x] 4.1 Run the corpus, triage every EFD038 finding, and record the result in `validation/FINDINGS.md`.
- [x] 4.2 Build the solution and run the full suite; verify zero warnings and all tests pass.
- [x] 4.3 Run `openspec validate add-efd038-stale-tracked-entities --strict`.
