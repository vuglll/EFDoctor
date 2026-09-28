# Design

## Context

`TryAnalyzeSourceCore` recognizes EF composition in two steps:

1. It checks that the method name is in `BoundNeutralEfMethods`.
2. It checks that the method's containing type is `EntityFrameworkQueryableExtensions`, or that the method is in the `Microsoft.EntityFrameworkCore` namespace (the relational extensions).

`ElementPreservingEfMethods`, which EFD017 and EFD025 use, already lists `AsTracking`, `IgnoreAutoIncludes`, and `IgnoreQueryFilters`.

## Decisions

### Extend the shared set rather than per rule

**Decision:** Add the three names to `BoundNeutralEfMethods`.

**Why:**
- All three methods return an `IQueryable<TEntity>` of the same element type, and none of them changes which rows the query can return, as far as EFD005's bound model is concerned.
- `IgnoreQueryFilters` can *widen* the result, by including soft-deleted or other-tenant rows. EFD005 never treats a global query filter as a bound, so its outcome is unchanged.
- Fixing this per rule would leave the two lists out of sync again.

**Alternative:** Make `BoundNeutralEfMethods` an alias of `ElementPreservingEfMethods`. This was rejected because the relational methods (`AsSplitQuery`/`AsSingleQuery`) live in a separate set that uses a different containing type. Keeping two explicit sets, with a test that asserts the EF-level names agree, is clearer.

### Guard against future drift

**Decision:** Add a unit test asserting that every name in `ElementPreservingEfMethods` is also in `BoundNeutralEfMethods`. The test reads `BoundNeutralEfMethods` through `InternalsVisibleTo` if it is available, and otherwise uses behavior-level parity fixtures, one per method.

## Risks / Trade-offs

- **[Risk]** New findings appear for projects that use these methods. → This is the intended recall. The corpus re-run triages them.
