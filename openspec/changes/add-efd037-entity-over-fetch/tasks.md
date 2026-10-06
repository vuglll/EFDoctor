# Tasks

## 1. Analyzer

- [ ] 1.1 Add `EntityOverFetchAnalyzer` (EFD037): materializer into a local, proven chain without projection or includes, the closed list of uses, counted properties, and the threshold; follow the shared analyzer conventions.
- [ ] 1.2 Add analyzer tests tagged to every scenario, with at least ten positive and ten negative fixtures; verify they pass.
- [ ] 1.3 Register the analyzer in `WorkspaceAnalyzer`, and add the `AnalyzerReleases.Unshipped.md` entry.

## 2. Fixtures and end-to-end

- [ ] 2.1 Add `tests/Fixtures/EFD037.Sample` to the solution under the existing Fixtures folder, with reported, silent, and suppressed cases; add a console and JSON end-to-end test with exact ranges.
- [ ] 2.2 Add a case to the shared test-project fixture, and update the expected count and rule order.

## 3. Documentation

- [ ] 3.1 Add `docs/rules/EFD037.md` and the boundary section in `docs/rules/README.md`.
- [ ] 3.2 Update the README rule table and status line, `PACKAGE.md`, the usage guide's severity table, the suppression guide, the development guide (verification map and implementation history), the roadmap, and the changelog.

## 4. Verification

- [ ] 4.1 Run the corpus, triage every EFD037 finding, and record the result in `validation/FINDINGS.md`; verify no false positive.
- [ ] 4.2 Build the solution and run the full suite; verify zero warnings and all tests pass.
- [ ] 4.3 Run `openspec validate add-efd037-entity-over-fetch --strict`.
