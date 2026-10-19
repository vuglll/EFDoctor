# Proposal

## Why

EFD010 reports a synchronous EF Core database call inside an async method, such as `db.Orders.ToList()` where `await db.Orders.ToListAsync()` would release the thread. The fix is mechanical, and the diagnostic already names the counterpart. This is the last fix of Phase 4.

## What Changes

- Add the **EFD010 code fix**, "Await {counterpart}". It replaces the reported call with an `await` of its async counterpart:
  - **Rename:** the method is renamed to the counterpart that EFD010 names (`ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`, `FindAsync`, …), keeping its arguments.
  - **Await:** the call is wrapped in `await`, and parenthesized when a member is read from its result, as in `(await q.ToListAsync()).Count`.
  - **Using directive:** `using Microsoft.EntityFrameworkCore;` is added when the counterpart is an EF Core extension method and the document lacks it.
  - **Token:** when a `CancellationToken` parameter is in scope, it is passed as the last argument, so the fixed code doesn't trip EFD016. `FindAsync` is the exception, because there the token would be taken as a key value.
- **The fix is offered only when the result compiles:**
  - the nearest enclosing method, lambda, or local function is `async`;
  - the call isn't inside a `lock` statement;
  - the counterpart exists;
  - the call is in reduced or instance form, not static form.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analyzer-code-fixes`: adds the EFD010 requirement.

## Impact

- **Code:** `AwaitAsyncCounterpartCodeFixProvider` in `EFDoctor.CodeFixes`.
- **Tests:** fix tests for query terminals, `SaveChanges`, `Find`, member access on the result, the using directive, the token, fix-all, and the withheld shapes, recompiling every result.
- **Docs:** `docs/rules/EFD010.md`, `PACKAGE.md`, the roadmap (Phase 4 complete), the development history, and the CHANGELOG.
