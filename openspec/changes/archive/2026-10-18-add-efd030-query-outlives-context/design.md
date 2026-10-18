# Design

## Context

EFD027 and EFD038 resolve a query's context through `EfContextIdentity.ContextOfDbSet`. The chain proof, with the origin proof for self-composed locals, finds the query's `DbSet` origin. EFD030 combines both, and adds a check on how the context local is declared.

## Goals / Non-Goals

**Goals:** report the disposed-context return with no false positives in the common shapes: a `using` declaration, a `using` statement, `await using`, and a DI scope.

**Non-Goals:**
- contexts disposed by explicit `Dispose()` calls, or in `finally` blocks;
- contexts returned from helper factories;
- queries stored in fields;
- cross-method flow.

## Decisions

### Entry point: the return operation

**Decision:** Register on `OperationKind.Return`, which excludes `yield return` because that has its own operation kind. Take the returned value, strip implicit conversions, and accept it when its type implements `IQueryable`, or when it is `AsEnumerable`/`AsAsyncEnumerable` over a value that does. Prove the query's origin with the origin proof enabled.

**Why:** Only the origin matters: whatever composition the query has, it runs on that context. A materialized value never implements `IQueryable`, so it's excluded without a list of materializers.

### Using scope

**Decision:** The context local is disposed at the return when either of these holds:
- **`using` declaration:** its declarator sits in an `IUsingDeclarationOperation`, and the return is a descendant of that declaration's enclosing block, after it;
- **`using` statement:** its declarator is in the `Resources` of an `IUsingOperation` whose `Body` contains the return.

In both cases the nearest enclosing function of the return, whether a method body, an anonymous function, or a local function, must also be the nearest enclosing function of the `using`.

**Why:** A return inside a lambda in the `using` scope may run while the context is alive. Restricting to the same function keeps the rule precise.

### DI scope

**Decision:** When the context local isn't itself a `using` local, also accept a local, never reassigned, whose initializer is `GetRequiredService<T>()` or `GetService<T>()`, called through either the extension or the instance form. The receiver must be `<scope>.ServiceProvider`, where `<scope>` is a `using` local that meets the same scope test.

**Why:** Background services commonly resolve a context from `CreateScope()` and dispose the scope, which disposes the scoped context.

## Risks / Trade-offs

- **[Risk] A caller materializes immediately, inside a lambda passed into the method, so the query never escapes.** → Not possible with a returned value. The disposal happens at the return.
- **[Trade-off] An explicit `Dispose()` in `finally`, and a context from a helper, are missed.** → Accepted for the first version, and documented.
