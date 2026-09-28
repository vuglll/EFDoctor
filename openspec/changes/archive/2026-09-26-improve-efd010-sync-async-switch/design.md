# Design

## Context

`SyncDatabaseCallInAsyncAnalyzer` decides on a `suggestion` (for example `FirstOrDefaultAsync`), then reports if the call is in an async context. It does not look at the surrounding control flow.

## Decisions

### Detect the switch by the awaited counterpart, not by the condition

**Decision:** Walk the ancestors of the synchronous call up to the enclosing lambda, local function, or method body. For each `IConditionalOperation` (which covers both `?:` and `if`), identify which arm contains the call. If the opposite arm contains an `IAwaitOperation` whose awaited operation invokes a method named `suggestion`, do not report. The awaited operation may be the call itself or a `ConfigureAwait` wrapper around it. The walk stops at the first boundary, so a conditional outside a lambda doesn't exempt a call inside that lambda.

**Why:** A condition named `async` is only a convention. What proves the author deliberately chose between sync and async is that the counterpart is awaited on the other branch. Requiring the exact counterpart name keeps `if (x) await Task.Delay(1); else ctx.Items.ToList();` reported.

**Alternative:** Matching a `bool` parameter named `async`. Rejected, because it is too convention-bound and would also exempt a branch that has no async counterpart.

## Risks / Trade-offs

- **[Risk]** `flag ? await q.ToListAsync() : other.ToList()` is a different query in each arm, and it is no longer reported. → The author has visibly written the async path, and this shape is rare. Accepted.
