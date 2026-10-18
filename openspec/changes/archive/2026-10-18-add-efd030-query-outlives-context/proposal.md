# Proposal

## Why

An EF Core query is deferred: it runs when the caller enumerates it. A method that creates a `DbContext` in a `using`, and returns a query over it, hands back a query whose context is disposed at the `return`:

```csharp
public IQueryable<Order> OpenOrders()
{
    using var db = _factory.CreateDbContext();
    return db.Orders.Where(o => o.Status == Status.Open); // disposed before the caller runs it
}
```

The caller's `ToList()` throws `ObjectDisposedException: Cannot access a disposed context instance`. The method compiles, and it looks like a clean repository method. The roadmap rates this high value, high detectability, and low false-positive risk.

## What Changes

- Add **EFD030, query returned after its `DbContext` is disposed**: `Reliability`, `Warning`, high confidence. It reports a `return` whose value is a deferred EF Core query when the query's `DbContext` is disposed as the method returns:
  - **Deferred values:**
    - an expression whose type implements `IQueryable`;
    - `AsEnumerable()` or `AsAsyncEnumerable()` over a query.
  - **Proving the query and its context:** the query is proven by the shared chain proof, including query locals and the origin proof. Its context is the instance its `DbSet` origin is read from.
  - **When the context counts as disposed:** the context is a local declared by a `using` declaration or `using` statement, synchronous or `await using`, and the `return` is inside that scope, in the same method, lambda, or local function.
  - **Contexts from a DI scope:** the context may also be a local resolved by `GetRequiredService<T>()` or `GetService<T>()` from the `ServiceProvider` of an `IServiceScope` declared in such a `using`. Disposing the scope disposes the context.
- **Not reported:**
  - a materialized result (`ToList()`, `ToArrayAsync()`, …);
  - a context that is a field, a parameter, or a local without `using`;
  - a `return` after the `using` statement's block ends;
  - a `return` inside a lambda or local function that is nested in the `using`;
  - iterator methods (`yield return`), whose `using` lasts until enumeration ends.

## Capabilities

### New Capabilities

- `efd030-query-outlives-context`: detection, boundaries, finding contract, suppression, and tests for EFD030.

### Modified Capabilities

None.

## Impact

- **Code:** a new `QueryOutlivesContextAnalyzer`, with registration in `WorkspaceAnalyzer` and `AnalyzerReleases.Unshipped.md`.
- **Tests:**
  - analyzer fixtures;
  - `tests/Fixtures/EFD030.Sample` with console and JSON end-to-end tests;
  - an `EFD005.TestSample` case.
- **Docs:** `docs/rules/EFD030.md`, the rules README, the README and `PACKAGE.md` tables, usage, suppression, the roadmap, the development history, and the CHANGELOG.
- **Validation:** a corpus re-run, with every finding triaged.
