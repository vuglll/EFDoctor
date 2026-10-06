# Tasks

## 1. Analyzer

- [x] 1.1 Follow a count through a local that is only compared for existence; verify positive tests for every comparison, sync and async, with and without a predicate, and negative tests for every other use.
- [x] 1.2 Report a null-checked `FirstOrDefault`/`FirstOrDefaultAsync` on an unprojected entity query, inline and through a local, at medium confidence, with its own message, impact, and remediation; verify positive and negative tests, including projections and the other element operators.
- [x] 1.3 Add the issue's test file as an acceptance test over EFD002 and EFD019; verify every method has exactly one finding with the expected rule, confidence, and recommendation.

## 2. Fixtures and end-to-end

- [x] 2.1 Add stored-count and `FirstOrDefault` cases to `EFD002.Sample`, and update the end-to-end test.

## 3. Documentation

- [x] 3.1 Update `docs/rules/EFD002.md`, the EFD002 boundary section in `docs/rules/README.md`, the README and PACKAGE.md rule rows and confidence, the usage guide's severity table, and the changelog.

## 4. Verification

- [x] 4.1 Run the corpus, triage new EFD002 findings, and record the result in `validation/FINDINGS.md`; verify no false positive.
- [x] 4.2 Build the solution and run the full suite; verify zero warnings and all tests pass.
- [x] 4.3 Run `openspec validate improve-efd002-stored-existence --strict`.
