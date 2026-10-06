# Tasks

## 1. Analyzer

- [ ] 1.1 Split EFD002's existence-comparison classifier into a shared internal method; verify the EFD002 tests pass unchanged.
- [ ] 1.2 Recommend `Any`/`AnyAsync` (or its negation) for inline count reducers in an existence comparison; verify tests for the six comparisons, both operand orders, predicates, and counts used as values.
- [ ] 1.3 Report a stored count-only local, with the `Any` or `Count` recommendation and the run-once note; verify positive tests for each read form and negative tests for every use that needs rows.
- [ ] 1.4 Make EFD005 yield to the stored-local finding; verify one finding per materializer.

## 2. Fixtures and end-to-end

- [ ] 2.1 Add stored-local and existence cases to `EFD019.Sample`, and update the end-to-end test's counts, ranges, and remediation assertions.

## 3. Documentation

- [ ] 3.1 Update `docs/rules/EFD019.md`, the EFD019 boundary section in `docs/rules/README.md`, the EFD005 page's yield note if it lists EFD019 shapes, and the changelog.

## 4. Verification

- [ ] 4.1 Run the corpus, triage new and changed EFD019 findings, and record the result in `validation/FINDINGS.md`; verify no false positive.
- [ ] 4.2 Build the solution and run the full suite; verify zero warnings and all tests pass.
- [ ] 4.3 Run `openspec validate improve-efd019-stored-count --strict`.
