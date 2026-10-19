# Proposal

## Why

EFD024 reports an `Include` whose collection a method only uses filtered in memory. Validation round 2 found a false positive in Kavita. `GetSeriesDetailPlusDto` filters `ExternalRecommendations` twice, with `r.SeriesId == null` and with `r.SeriesId != null`. Together the two filters keep every row, so a filtered `Include` can't narrow the load.

The round-2 notes proposed reporting only when every use applies the same predicate. That would also drop real findings, such as `Any(p => p.Published)` with `FirstOrDefault(p => p.Pinned)`, where `Include(b => b.Posts.Where(p => p.Published || p.Pinned))` still helps. This change targets the complementary shape instead.

## What Changes

- EFD024 doesn't report when two of the predicates are complementary, compared by their tokens with the lambda parameter renamed:
  - `e` and `!e`;
  - the same operands compared with `==` and `!=`, `<` and `>=`, or `>` and `<=`.
- Different filters that aren't complementary are still reported.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd024-include-filtered-in-memory`: "Detect an Include whose collection is only used filtered" adds the complementary-filter condition, with two scenarios.

## Impact

- **Code:** `IncludeFilteredInMemoryAnalyzer`.
- **Tests:** complementary pairs (`!`, null checks with different parameter names, a parenthesized range, a negated conjunction), and non-complementary pairs that are still reported.
- **Docs:** `docs/rules/EFD024.md`, the rules README, and the CHANGELOG.
