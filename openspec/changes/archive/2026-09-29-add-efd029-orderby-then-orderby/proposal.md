# Proposal

## Why

`query.OrderBy(a).OrderBy(b)` reads as "sort by `a`, then by `b`", but it doesn't do that. When EF Core translates a second `OrderBy` or `OrderByDescending` onto a query that is already ordered, it discards the earlier ordering, so the SQL sorts by `b` alone. The mistake compiles, passes review, and silently returns rows in the wrong order. Ties on `b` come back in an order the database doesn't guarantee, which also breaks paging built on the sort. The roadmap lists EFD029 as a "Next" candidate: high value, a single semantically resolved trigger, and very low false-positive risk.

## What Changes

- Add EFD029. It reports a `System.Linq.Queryable.OrderBy` or `OrderByDescending` call whose source is already ordered in the same inline chain: an earlier `OrderBy`, `OrderByDescending`, `ThenBy`, or `ThenByDescending` reached only through composition that doesn't change the ordering's meaning. That composition is filtering, include, tracking, query-filter, split-query, and tagging operators. The chain must be proven to start at an EF Core `DbSet<T>` or `DbContext.Set<T>()`.
- Stay silent when an operator between the two orderings gives the first ordering a purpose, or changes the query's shape: `Skip`, `Take`, `Distinct`, `Select`, `GroupBy`, set operations, joins, and anything else not on the pass-through list. For example, `OrderBy(a).Take(10).OrderBy(b)` sorts the top ten by `b`, and it is legitimate.
- Stay silent on in-memory `Enumerable` ordering, unproven `IQueryable` sources, orderings that reach the replacing call through a local variable, and same-named methods that don't resolve to `Queryable`.
- Emit one high-confidence, `Warning`-severity `Correctness` finding for each replacing call. Its evidence names the discarded ordering operators and the operator that replaces them. The remediation is `ThenBy`/`ThenByDescending` when both keys were meant, or deleting the earlier ordering when replacing it was intended.
- Register EFD029 in CLI workspace analysis. Add analyzer, fixture, suppression, console, JSON, documentation, release-metadata, README, and CHANGELOG coverage. The report schema doesn't change.

## Capabilities

### New Capabilities

- `efd029-orderby-replaces-ordering`: Defines detection, exclusions, evidence, remediation, suppression, and validation for a query whose second `OrderBy`/`OrderByDescending` discards an earlier ordering in the same inline EF Core chain.

### Modified Capabilities

None. The existing `cli-analysis` requirement to run every shipped analyzer covers EFD029, and the repository consistency checks enforce its registration.

## Impact

The change adds one Roslyn analyzer (`OrderByReplacesOrderingAnalyzer`), its analyzer tests, an `EFD029.Sample` CLI fixture, a case in the shared test-project fixture, `docs/rules/EFD029.md`, the CLI analyzer registration, an `AnalyzerReleases.Unshipped.md` entry, and README, package README, development, suppression, rules-index, roadmap, and CHANGELOG updates. The rule is provider-agnostic and local-only. It adds no dependency or code fix, and it doesn't change the versioned JSON schema.
