# Proposal

## Why

EFD019 reports a query that is fully materialized only to reduce it on the client: `db.Orders.ToList().Count()`, `(await db.Orders.ToListAsync()).First(o => o.Open)`. The fix is mechanical: drop the materialization, and apply the reducer to the query, in its async form when the materialization was awaited. This is the third fix of Phase 4.

## What Changes

- Add the **EFD019 code fix**, "Reduce in the query". For a reported `ToList`/`ToArray` (or the awaited `ToListAsync`/`ToArrayAsync`) whose result is directly reduced:
  - **Synchronous:** `q.ToList().Reducer(args)` becomes `q.Reducer(args)`. `q.ToList().Count` and `q.ToArray().Length` become `q.Count()`.
  - **Awaited:** `(await q.ToListAsync(token)).Reducer(args)` becomes `await q.ReducerAsync(args, token)`. `.Count` and `.Length` become `await q.CountAsync(token)`. The materializer's cancellation token moves to the async reducer.
- The fix isn't offered for:
  - a materialization stored in a local (EFD019's stored-count shape);
  - the static `Enumerable.Reducer(q.ToList())` form, which would need its containing type rewritten.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analyzer-code-fixes`: adds the EFD019 requirement.

## Impact

- **Code:** `ReduceInQueryCodeFixProvider` in `EFDoctor.CodeFixes`.
- **Tests:** fix tests for the sync, property, and awaited forms, fix-all, and the withheld shapes, recompiling every result.
- **Docs:** `docs/rules/EFD019.md`, `PACKAGE.md`, the roadmap, the development history, and the CHANGELOG.
