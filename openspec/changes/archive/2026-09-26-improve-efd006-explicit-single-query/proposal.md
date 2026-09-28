# Proposal

## Why

Round-1 corpus validation (`validation/FINDINGS.md`, defect 7) found one EFD006 false positive: Jellyfin's `UserManager` loads a user with several collection includes and calls `.AsSingleQuery()` explicitly. The current spec keeps `AsSingleQuery` reportable. That contradicts EF Core's own behavior.

EF Core logs `MultipleCollectionIncludeWarning` only when a query loads several collections and no splitting behavior was chosen. It does not log the warning when you call `AsSingleQuery()` explicitly. EF treats that call as the developer acknowledging the single-query trade-off, for example to get a consistent snapshot without a transaction. EFD006 should honor the same signal.

## What Changes

- **BREAKING (rule behavior):** EFD006 no longer reports a query chain that contains a resolved EF Core `AsSingleQuery`, just as it already skips `AsSplitQuery`.
- The finding's evidence now says that no explicit query-splitting mode occurs in the chain.
- A global `UseQuerySplittingBehavior` configuration is still not followed. That remains out of scope, as before.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd006-multiple-collection-include`: "Respect split-query and client boundaries" treats an explicit `AsSingleQuery` as an opt-out.

## Impact

- **Code:** `MultipleCollectionIncludeAnalyzer`.
- **Tests:**
  - The `AsSingleQuery` positive fixture becomes a negative.
  - A client-boundary fixture is added.
  - Every EFD006 scenario gets spec traits. This is the first time the capability is traced.
- **Docs:** `docs/rules/EFD006.md`, the README EFD006 boundary, and the CHANGELOG.
- **Validation:** re-run Jellyfin. The `UserManager` finding should disappear.
