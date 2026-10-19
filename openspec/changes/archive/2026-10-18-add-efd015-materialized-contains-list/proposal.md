# Proposal

## Why

A common two-step pattern loads a list of keys from one query, then filters a second query with it:

```csharp
var customerIds = await db.Customers.Where(c => c.Region == region).Select(c => c.Id).ToListAsync();
var orders = await db.Orders.Where(o => customerIds.Contains(o.CustomerId)).ToListAsync();
```

The first query brings every matching key to the client, and the second sends them all back as parameters. The list has no bound, so it can be very large. Before EF Core 8, it was inlined as constants, one plan per list. Since EF Core 8, it is a JSON parameter the optimizer can't estimate. SQL Server also limits a command to 2,100 parameters on providers and modes that use them. Keeping the first query as an `IQueryable` lets EF Core compose one statement, `WHERE CustomerId IN (SELECT Id FROM Customers WHERE …)`, in one round trip. The roadmap lists this shape as EFD015.

## What Changes

- Add **EFD015, materialized key list used in another query's `Contains`**: `Performance`, `Info`, advisory. It reports a `Contains` call inside a SQL-translated query lambda when all of these hold:
  - **The query:** the lambda is an argument of a `Queryable` operator over a proven EF Core query.
  - **The call:** its collection is a local, and the value it tests reads the lambda's parameter.
  - **The local:** its value at the read is statically determined. It is `ToList()`, `ToArray()`, or `ToHashSet()`, or the awaited async form, over a proven EF Core query with no strong row bound: no `Take`, and no key equality.
  - **The context:** both queries' `DbContext` instances are the same, proven by symbol.
  - **Other uses:** every reference to the local is the collection of a `Contains` call.
- **Not reported:**
  - lists built in memory or passed in;
  - bounded inner queries (`Take`, key lookups);
  - lists also used for anything else (counted, enumerated, returned);
  - queries on different contexts, where a subquery is impossible.

## Capabilities

### New Capabilities

- `efd015-materialized-contains-list`: detection, boundaries, finding contract, suppression, and tests for EFD015.

### Modified Capabilities

None.

## Impact

- **Code:** a new `MaterializedContainsListAnalyzer`, with registration and the release entry.
- **Tests:** analyzer fixtures, `tests/Fixtures/EFD015.Sample` with end-to-end tests, and an `EFD005.TestSample` case.
- **Docs:** `docs/rules/EFD015.md`, the rules README, the README, `PACKAGE.md`, usage, suppression, the roadmap, the development history, and the CHANGELOG.
- **Validation:** a corpus re-run, with every finding triaged.
