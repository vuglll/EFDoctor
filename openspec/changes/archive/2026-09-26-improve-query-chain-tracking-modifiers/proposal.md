# Proposal

## Why

The round-1 validation found a recall gap (`validation/FINDINGS.md`, "Recall gap"). The shared inline query-chain proof, `EfQueryOperationAnalysis.TryAnalyzeSource`, accepts only a fixed set of EF Core composition methods between the `DbSet` and the terminal operator. The set includes `AsNoTracking`, but it lacks these three:

- `AsTracking`
- `IgnoreQueryFilters`
- `IgnoreAutoIncludes`

Any query that uses one of them fails the origin proof, so every rule built on that proof stays silent. OpenIddict's EF Core stores put `.AsTracking()` on almost every query, which is why OpenIddict produced **0 findings**. That result says nothing about the code; for example, one store loads every authorization for an application, unpaged. Across the rest of the corpus, `IgnoreQueryFilters` and `IgnoreAutoIncludes` appear 58 times.

The element-preserving set that EFD017 and EFD025 use already includes all three methods, so the two lists disagree.

## What Changes

- The shared chain proof accepts resolved EF Core `AsTracking` (both overloads), `IgnoreQueryFilters` (both overloads), and `IgnoreAutoIncludes` as element-preserving composition. They are bound-neutral for EFD005: none of them establishes, removes, or changes a row bound.
- **Affected rules:** every rule that uses the chain proof now also sees queries with these methods. That is EFD004, EFD005, EFD009, EFD010, EFD013, EFD014, EFD019, EFD020, EFD022, and EFD023. Their contracts already say "resolved EF Core composition", so only EFD005, which lists the bound-neutral methods by name, needs a spec change.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd005-unbounded-query-materialization`: "Bound-neutral composition does not change the outcome" adds `AsTracking`, `IgnoreQueryFilters`, `IgnoreAutoIncludes`, and `TagWithCallSite` to its list. The code already accepts `TagWithCallSite`, but the spec didn't list it.

## Impact

- **Code:** `EfQueryOperationAnalysis.BoundNeutralEfMethods` gains three names. No analyzer changes.
- **Tests:**
  - EFD005 parity fixtures for each method.
  - Recall fixtures for EFD004 and EFD019, to show the proof now passes through the new methods.
  - Tracing for the changed EFD005 scenarios.
- **Docs:** `docs/rules/EFD005.md`, the README chain-proof wording where it lists methods, and the CHANGELOG.
- **Validation:** re-run the whole corpus. Expect new findings in OpenIddict and in any code that uses these methods. Triage the new findings.
