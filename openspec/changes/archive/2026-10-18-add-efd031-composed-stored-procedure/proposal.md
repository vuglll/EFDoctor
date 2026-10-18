# Proposal

## Why

`FromSqlRaw`, `FromSql`, and `FromSqlInterpolated` let EF Core compose LINQ over raw SQL by wrapping it in a subquery. A stored-procedure call can't be wrapped:

```csharp
var open = db.Orders
    .FromSqlRaw("EXEC dbo.GetOrders @p0", customerId)
    .Where(o => o.Open)          // throws: non-composable SQL
    .ToList();
```

EF Core throws "FromSqlRaw or FromSqlInterpolated was called with non-composable SQL and with a query composing over it". The query compiles, and the failure appears only at runtime. The roadmap rates this cheap and precise, with very low false-positive risk.

## What Changes

- Add **EFD031, LINQ composed over a stored-procedure call**: `Correctness`, `Warning`, high confidence. It reports a query operator that EF Core would compose into SQL, when its source is a resolved `FromSqlRaw`, `FromSql`, or `FromSqlInterpolated` call whose SQL text begins with `EXEC`, `EXECUTE`, or `CALL`:
  - **SQL text:** the constant SQL argument, or the leading literal text of an interpolated SQL argument.
  - **Composing operators:**
    - every `Queryable` operator other than `AsQueryable`;
    - EF Core `Include`/`ThenInclude`;
    - EF Core async terminals other than `ToListAsync`, `ToArrayAsync`, `ToDictionaryAsync`, `ToHashSetAsync`, `ForEachAsync`, `LoadAsync`, and `AsAsyncEnumerable`.
  - **Pass-through between the call and the operator:** only `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `IgnoreQueryFilters`, `IgnoreAutoIncludes`, `TagWith`, and `TagWithCallSite`, and query locals whose value is statically determined.
- **Not reported:**
  - materializing the result directly (`ToList`, `ToArray`, `AsEnumerable`, `foreach`, `ToListAsync`, …);
  - composing after `AsEnumerable()`;
  - composable SQL such as `SELECT * FROM dbo.GetOrdersFn(@p0)`;
  - SQL whose leading text isn't known.

## Capabilities

### New Capabilities

- `efd031-composed-stored-procedure`: detection, boundaries, finding contract, suppression, and tests for EFD031.

### Modified Capabilities

None.

## Impact

- **Code:** a new `ComposedStoredProcedureAnalyzer`, with registration in `WorkspaceAnalyzer` and the release file.
- **Tests:**
  - analyzer fixtures;
  - `tests/Fixtures/EFD031.Sample` with end-to-end tests;
  - an `EFD005.TestSample` case.
- **Docs:** `docs/rules/EFD031.md`, the rules README, the README, `PACKAGE.md`, usage, suppression, the roadmap, the development history, and the CHANGELOG.
- **Validation:** a corpus re-run, with every finding triaged.
