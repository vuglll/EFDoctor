# Tasks

## 1. Opt-out

- [x] 1.1 Treat a resolved `AsSingleQuery` like `AsSplitQuery`, and update the evidence text. Verify that the `AsSingleQuery` fixture (before and after the includes) is not reported.
- [x] 1.2 Update the analyzer `description:` if it mentions split query only, so it doesn't contradict the spec.

## 2. Tests and tracing

- [x] 2.1 Add an explicit client-boundary fixture: `AsEnumerable().AsQueryable()` followed by includes, which is not reported.
- [x] 2.2 Tag every `efd006-multiple-collection-include` scenario with a `Spec` trait, including the end-to-end test. Verify that `RepositoryConsistencyTests` passes after the sync.

## 3. Documentation

- [x] 3.1 Update `docs/rules/EFD006.md`, the README EFD006 boundary, and the CHANGELOG (Unreleased).

## 4. Verification

- [x] 4.1 Build with no warnings and run the full suite.
- [x] 4.2 Re-run the corpus for Jellyfin and Bitwarden. Confirm that the `UserManager` finding is gone and the Bitwarden finding remains. Update `validation/FINDINGS.md` and regenerate `SUMMARY.md`.
- [x] 4.3 Run `openspec validate improve-efd006-explicit-single-query --strict`.
