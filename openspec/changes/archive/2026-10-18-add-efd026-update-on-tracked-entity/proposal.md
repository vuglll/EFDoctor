# Proposal

## Why

`DbContext.Update(entity)` exists for *detached* entities: it attaches the entity and marks every property as modified. Calling it on an entity the context already tracks is a common habit carried over from other data layers:

```csharp
var order = await db.Orders.FirstAsync(o => o.Id == id);
order.Status = OrderStatus.Shipped;
db.Update(order);              // unnecessary: the change tracker already saw Status change
await db.SaveChangesAsync();   // now writes every column, not just Status
```

The change tracker already detected the change. `Update` turns a one-column `UPDATE` into a full-row one: more data written, more triggers and replication traffic, and lost concurrency between writers of different columns. The roadmap lists this shape as an advisory EFD026.

## What Changes

- Add **EFD026, `Update` on an entity the context already tracks**: `Performance`, `Info`, advisory. It reports `DbContext.Update(entity)`, `Update<T>(entity)`, or `DbSet<T>.Update(entity)` when all of these hold:
  - **The local:** the argument is a local declared, in an earlier statement of an enclosing block in the same function, by a tracked load. That load is `Find`/`FindAsync`, or a single-entity terminal (`First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, or the awaited async form) over a proven EF Core query without `AsNoTracking`.
  - **The context:** the load and the `Update` run on the same `DbContext` instance, proven by symbol.
  - **No tracker changes:** nothing between them clears the tracker (`ChangeTracker.Clear()`), detaches entities (`Entry(…).State = EntityState.Detached`), or reassigns the local.
- **Not reported:**
  - entities from parameters, DTO mapping, or untracked loads, which are `Update`'s real use;
  - a load on another context.

## Capabilities

### New Capabilities

- `efd026-update-on-tracked-entity`: detection, boundaries, finding contract, suppression, and tests for EFD026.

### Modified Capabilities

None.

## Impact

- **Code:** a new `UpdateOnTrackedEntityAnalyzer`, with registration and the release entry.
- **Tests:** analyzer fixtures, `tests/Fixtures/EFD026.Sample` with end-to-end tests, and an `EFD005.TestSample` case.
- **Docs:** `docs/rules/EFD026.md`, and every rule list and table.
- **Validation:** a corpus re-run, with every finding triaged.
