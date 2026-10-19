# Proposal

## Why

EF Core's async methods take an optional `CancellationToken`. When a method has a token, for example an ASP.NET Core action's `CancellationToken`, a background service's `stoppingToken`, or a handler's token, but doesn't pass it on, the database command keeps running after the request is aborted or the host shuts down:

```csharp
public async Task<List<Order>> Search(string term, CancellationToken cancellationToken)
{
    return await db.Orders.Where(o => o.Name.StartsWith(term)).ToListAsync(); // token not passed
}
```

The query holds a connection and the database's resources until it completes, even though nobody waits for the result. Under load, abandoned queries pile up. The roadmap lists this shape as an advisory EFD016.

## What Changes

- Add **EFD016, `CancellationToken` in scope not passed to an EF Core async call**: `Reliability`, `Info`, advisory. It reports a resolved EF Core async operation that doesn't receive a token while a `CancellationToken` parameter is in scope:
  - **The operations:** async query terminals and bulk operations on `EntityFrameworkQueryableExtensions` and `RelationalQueryableExtensions`, `DbContext.SaveChangesAsync` and its overrides, and `DbSet<T>.FindAsync`.
  - **Missing token:** the invocation's `CancellationToken` parameter takes its default value implicitly. For `FindAsync`, the `params object[]` overload without a token counts too.
  - **Token in scope:** a parameter of type `CancellationToken` of the containing method, or of an enclosing lambda or local function.
- **Not reported:**
  - an explicit `CancellationToken.None` or `default`, which is a deliberate choice;
  - methods without a token parameter in scope;
  - tokens held only in fields or locals.

## Capabilities

### New Capabilities

- `efd016-cancellation-token-not-passed`: detection, boundaries, finding contract, suppression, and tests for EFD016.

### Modified Capabilities

None.

## Impact

- **Code:** a new `CancellationTokenNotPassedAnalyzer`, with registration and the release entry.
- **Tests:** analyzer fixtures, `tests/Fixtures/EFD016.Sample` with end-to-end tests, and an `EFD005.TestSample` case.
- **Docs:** `docs/rules/EFD016.md`, and every rule list and table.
- **Validation:** a corpus re-run, with every finding triaged.
