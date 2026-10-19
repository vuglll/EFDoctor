# Proposal

## Why

EFD036 judged each predicate alone. A query's filters combine in SQL, so a query that filters on an indexed column in one `Where` and on an unindexed one in another can seek the index. Validation round 2 found the false positive in Kavita: `Where(s => s.SeriesId == seriesId).AnyAsync(s => s.ValidUntilUtc >= DateTime.UtcNow)`, where `SeriesId` is indexed. Kavita's `GetAllCollectionsForSyncing` also showed the duplicate: two unindexed `Where` calls on one query gave two findings.

## What Changes

- The predicates traced to the same query origin are one query. EFD036 judges it on the filter columns of all its predicates.
- It reports the query once, at the first filter column of its first predicate.
- Separate queries, each with its own origin, stay separate.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd036-unindexed-predicate-column`: "Detect a predicate whose filter columns have no index" combines a query's predicates, with three scenarios.

## Impact

- **Code:** `UnindexedPredicateColumnAnalyzer`.
- **Tests:** chain cases for an indexed filter in another `Where` or in a terminal, a two-predicate query reported once, and two separate queries.
- **Docs:** `docs/rules/EFD036.md`, the rules README, and the CHANGELOG.
