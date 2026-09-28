# Design

## Context

`MultipleCollectionIncludeAnalyzer.TryAnalyzeChain` sets `hasSplitQuery` when a `RelationalQueryableExtensions` method named `AsSplitQuery` appears in the inline chain. The analysis stops when that flag is set.

## Decisions

### Treat any explicit splitting mode as an opt-out

**Decision:** Rename the flag to `hasExplicitSplittingMode`, and set it for both `AsSplitQuery` and `AsSingleQuery` on `RelationalQueryableExtensions`.

**Why:** This mirrors when EF Core itself emits `MultipleCollectionIncludeWarning`. See the proposal.

**Alternative:** Keep `AsSingleQuery` reportable at a lower confidence. Rejected, because the developer has made the choice in code, and a suppression would say the same thing with more noise.

## Risks / Trade-offs

- **[Risk]** Someone adds `AsSingleQuery()` only to silence EF's warning, without understanding it. → Explicit code is the strongest intent signal available to a static analyzer, and EF draws the same line.
