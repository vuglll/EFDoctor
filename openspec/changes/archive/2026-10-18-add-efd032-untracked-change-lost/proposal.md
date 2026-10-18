# Proposal

## Why

`AsNoTracking()` is a common default for reads, and it's easy to reuse such a read for an update:

```csharp
var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == id);
order.Status = OrderStatus.Shipped;
await db.SaveChangesAsync(); // saves nothing: 'order' isn't tracked
```

The change tracker never sees the entity, so `SaveChanges` writes nothing. The method succeeds, and no exception or warning is raised. The update is silently lost. The roadmap rates this high value. Its detection needed the analysis to follow query *results* through locals, which EFD037 and EFD038 added.

## What Changes

- Add **EFD032, change to an untracked entity is never saved**: `Correctness`, `Warning`, high confidence. It reports an assignment to a property of an entity loaded without tracking when all of these hold:
  - **The load:** a local is initialized, in a declaration statement, by a single-entity terminal (`First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, or their awaited async forms) over a proven EF Core query. Alternatively, the local is initialized by `ToList`, `ToArray`, or their awaited async forms, and a later `foreach` iterates it directly.
  - **The query:** its proven chain contains `AsNoTracking()` or `AsNoTrackingWithIdentityResolution()`.
  - **The change:** a later statement in the same block assigns a property of the entity, or of the `foreach` variable, with a simple, compound, or increment assignment.
  - **The save:** a later statement calls `SaveChanges` or `SaveChangesAsync` on the same `DbContext` instance, proven by symbol.
  - **Nothing attaches the entity:** between the load and the save, the entity and the list are used only for property reads and writes, or as the `foreach` source.
- **Not reported:**
  - any other use of the entity or the list, which might attach it. This covers passing it to any method (including `Update`, `Attach`, `Entry`, and `Add`), returning it, storing it, capturing it in a lambda, or reassigning the local;
  - a save on another context, or no save in the method;
  - a tracked load.

## Capabilities

### New Capabilities

- `efd032-untracked-change-lost`: detection, boundaries, finding contract, suppression, and tests for EFD032.

### Modified Capabilities

None.

## Impact

- **Code:** a new `UntrackedChangeLostAnalyzer`, with registration in `WorkspaceAnalyzer` and `AnalyzerReleases.Unshipped.md`.
- **Tests:**
  - analyzer fixtures;
  - `tests/Fixtures/EFD032.Sample` with end-to-end tests;
  - an `EFD005.TestSample` case.
- **Docs:** `docs/rules/EFD032.md`, the rules README, the README, `PACKAGE.md`, usage, suppression, the roadmap, the development history, and the CHANGELOG.
- **Validation:** a corpus re-run, with every finding triaged.
