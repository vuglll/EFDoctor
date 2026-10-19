## ADDED Requirements

### Requirement: Move a client-side reduction into the query
The EFD019 fix SHALL remove a reported materializer whose result is directly reduced, and apply the reduction to the query instead:
- **Synchronous:** for `ToList` or `ToArray`, the reducer call `source.ToList().Reducer(args)` SHALL become `source.Reducer(args)`. The `Count` and `Length` properties SHALL become `source.Count()`.
- **Awaited:** for `ToListAsync` or `ToArrayAsync`, the parenthesized await `(await source.ToListAsync(token)).Reducer(args)` SHALL become `await source.ReducerAsync(args, token)`. The `Count` and `Length` properties SHALL become `await source.CountAsync(token)`. A cancellation token passed to the materializer SHALL be passed to the async reducer.

The fix SHALL NOT be offered when the materialized result is stored in a local, or passed to a static `Enumerable` call.

#### Scenario: Synchronous reduction
- **WHEN** EFD019 reports `ToList()` in `db.Orders.ToList().First(o => o.Open)`, or `db.Orders.ToArray().Length`
- **THEN** the fix produces `db.Orders.First(o => o.Open)`, or `db.Orders.Count()`

#### Scenario: Awaited reduction with a token
- **WHEN** EFD019 reports `ToListAsync(token)` in `(await db.Orders.ToListAsync(token)).Any(o => o.Open)`, or in `(await db.Orders.ToListAsync()).Count`
- **THEN** the fix produces `await db.Orders.AnyAsync(o => o.Open, token)`, or `await db.Orders.CountAsync()`

#### Scenario: Stored or statically reduced result
- **WHEN** EFD019 reports a materialization stored in a local, or passed to `Enumerable.Count(...)` in static form
- **THEN** the fix is not offered
