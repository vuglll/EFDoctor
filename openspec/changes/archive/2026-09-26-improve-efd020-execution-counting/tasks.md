# Tasks

## 1. Execution counting

- [x] 1.1 Skip uses inside expression-tree lambdas. Verify with the subquery fixtures, which are not reported, and a delegate-lambda fixture, which is reported.
- [x] 1.2 Skip terminal invocations that have a `predicate` argument. Verify with predicate-only fixtures, which are not reported, and a `Sum(selector)` plus `ToList()` fixture, which is reported.
- [x] 1.3 Count the maximum executions on one path through conditionals. Verify with the ternary and `if`/`else` fixtures, which are not reported, and with an `Any()` followed by an `if`/`else`, which is reported.
- [x] 1.4 Update the analyzer `description:` so it doesn't contradict the spec.

## 2. Tests and tracing

- [x] 2.1 Tag every `efd020-multiple-enumeration` scenario with a `Spec` trait, including existing ones and the end-to-end test. Verify that `RepositoryConsistencyTests` passes after the sync.

## 3. Documentation

- [x] 3.1 Update `docs/rules/EFD020.md`, the README EFD020 boundary, and the CHANGELOG (Unreleased).

## 4. Verification

- [x] 4.1 Build with no warnings and run the full test suite.
- [x] 4.2 Re-run the corpus for Jellyfin and Smartstore. Confirm that the 16 false positives are gone and the `acceptable` finding remains. Update `validation/FINDINGS.md` and regenerate `SUMMARY.md`.
- [x] 4.3 Run `openspec validate improve-efd020-execution-counting --strict`.
