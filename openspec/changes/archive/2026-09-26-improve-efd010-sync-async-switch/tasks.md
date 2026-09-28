# Tasks

## 1. Switch exclusion

- [x] 1.1 Skip a synchronous call when the opposite arm of an enclosing conditional awaits the suggested counterpart, directly or through `ConfigureAwait`. Verify with the ternary and `if`/`else` fixtures, which are not reported, and an `if`/`else` whose other arm awaits something else, which is reported.
- [x] 1.2 Stop the walk at lambda and local-function boundaries. Verify with a fixture where an async lambda inside a switch arm is still reported.
- [x] 1.3 Update the analyzer `description:` so it does not contradict the spec.

## 2. Tests and tracing

- [x] 2.1 Tag every `efd010-sync-db-call-in-async` scenario with a `Spec` trait, including the end-to-end test. Verify that `RepositoryConsistencyTests` passes after the sync.

## 3. Documentation

- [x] 3.1 Update `docs/rules/EFD010.md`, the README EFD010 boundary, and the CHANGELOG (Unreleased).

## 4. Verification

- [x] 4.1 Build with no warnings and run the full suite.
- [x] 4.2 Re-run the corpus for Smartstore and eShop. Confirm the 5 switch false positives are gone and the other findings remain. Update `validation/FINDINGS.md` and regenerate `SUMMARY.md`.
- [x] 4.3 Run `openspec validate improve-efd010-sync-async-switch --strict`.
