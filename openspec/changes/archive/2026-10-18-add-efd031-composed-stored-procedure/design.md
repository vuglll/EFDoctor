# Design

## Context

EFD012 already resolves `FromSqlRaw` on `RelationalQueryableExtensions`, and reads its SQL argument. EFD031 reads the same argument for its leading keyword, and looks at what is composed on the result.

## Goals / Non-Goals

**Goals:** report the composition that EF Core always rejects over `EXEC`/`EXECUTE`/`CALL`.

**Non-Goals:**
- providers with other procedure syntax;
- SQL assembled at runtime;
- composition that crosses method boundaries.

## Decisions

### Entry point: the composing operator

**Decision:** Register on invocations. For a composing operator, walk its source down through the pass-through EF methods and statically determined locals to a `FromSql` call, and stop at anything else.

**Why:** The finding belongs at the operator that makes the query invalid. Only the first operator over the call is reported, because each later operator's source is an operator, not the call.

### Leading keyword only

**Decision:** Match `^\s*(EXEC|EXECUTE|CALL)\b`, ignoring case, on the constant SQL or the first literal part of an interpolated string.

**Why:** The keyword alone decides composability. A procedure name without `EXEC` is also non-composable on SQL Server, but it can't be told apart from a table or view name without guessing.

## Risks / Trade-offs

- **[Trade-off] `"dbo.GetOrders"` without `EXEC` is missed.** → Accepted for precision.
