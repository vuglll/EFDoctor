# Tasks

## 1. Chain proof

- [x] 1.1 Add `AsTracking`, `IgnoreQueryFilters`, and `IgnoreAutoIncludes` to `EfQueryOperationAnalysis.BoundNeutralEfMethods`. Verify it with EFD005 parity fixtures: a bounded query stays unreported, and an unbounded query is reported, for each method, including the `AsTracking(QueryTrackingBehavior)` and `IgnoreQueryFilters(names)` overloads if they are available.
- [x] 1.2 Add EFD004 and EFD019 recall fixtures that include `AsTracking()` and `IgnoreQueryFilters()`, and verify that they report.
- [x] 1.3 Add a guard test that every EF method in `ElementPreservingEfMethods` is accepted by the chain proof.

## 2. Tracing and docs

- [x] 2.1 Trace the changed EFD005 scenarios, and verify that `RepositoryConsistencyTests` passes after the sync.
- [x] 2.2 Update `docs/rules/EFD005.md`, any README wording that lists bound-neutral methods, and the CHANGELOG (Unreleased).

## 3. Verification

- [x] 3.1 Build with no warnings and run the full suite.
- [x] 3.2 Re-run the full corpus. Triage every new finding caused by the change, including OpenIddict's, then update `validation/FINDINGS.md` and regenerate `SUMMARY.md`.
- [x] 3.3 Run `openspec validate improve-query-chain-tracking-modifiers --strict`.
