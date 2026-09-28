# Design

## Context

See `proposal.md` for motivation. EFDoctor analyzers resolve EF Core markers in a compilation-start action and register per-operation actions. `EfQueryOperationAnalysis` exposes public `TryAnalyzeSource` (traces an operation's inline source to a `DbSet` origin), `NormalizeMethod`, `GetInvocationSource`, and `IsOrDerivesFrom`, all reused unchanged. EFD011 already covers the inverse mistake — `.Result`/`.Wait()`/`.GetAwaiter().GetResult()` blocking on an async call; EFD010 covers choosing the synchronous API while in an async context.

## Goals / Non-Goals

**Goals:**

- Detect a synchronous EF database call in an async method/lambda that has a direct EF async counterpart, with low false positives.
- Name the specific `*Async` replacement in the finding.
- Reuse the shared tracer; no edits to shared analysis code.

**Non-Goals:**

- Flagging synchronous calls in non-async methods (even `Task`-returning ones without the `async` keyword); requiring `async` keeps the "just await it" fix trivially valid and false positives low.
- Rewriting the call or proving the method is on a hot request path.
- Overlapping with EFD011 (blocking on async); the two target opposite mistakes.

## Decisions

### 1. Anchor on the sync call; require an async context

Register `OperationKind.Invocation`. A candidate is one of: a LINQ terminal in the recognized set whose `GetInvocationSource` traces via `TryAnalyzeSource` to a `DbSet`; `SaveChanges` whose receiver type `IsOrDerivesFrom` `DbContext`; or `Find` whose receiver type `IsOrDerivesFrom` `DbSet`. Report only when the nearest enclosing lambda or method is `async`, determined by walking ancestors to the first `IAnonymousFunctionOperation` (and using its `Symbol.IsAsync`) or, if none, the containing `IMethodSymbol.IsAsync`. Anchoring on the call gives a precise location and lets the finding name the exact `*Async` replacement (method name + `"Async"`, or `SaveChangesAsync`/`FindAsync`).

Requiring `async` on the immediate enclosing scope means a synchronous lambda inside an async method is correctly not flagged (you cannot await there), and a synchronous call in a plain method is left to a future, higher-effort variant.

### 2. Async-counterpart and source gating keep FP low

Terminals are gated by `TryAnalyzeSource` reaching a `DbSet`, which excludes in-memory `Enumerable` calls and arbitrary `IQueryable` parameters. `SaveChanges`/`Find` are gated by the receiver deriving from `DbContext`/`DbSet`. All in-scope methods have documented EF async counterparts, so the suggested fix always exists. Calls that are already `*Async` are not in the recognized sync set, so they are never flagged.

### 3. Confidence and relationship to EFD011

Medium confidence: a synchronous EF call in an async method is almost always better awaited, but a synchronous call can be acceptable off the hot path, so the rule downgrades rather than over-claims. EFD010 and EFD011 are complementary and do not double-report: EFD011 fires on `.Result`/`.Wait()` over an async call; EFD010 fires on the synchronous API itself. The central test-project downgrade still applies.

## Risks / Trade-offs

- **[A sync call in an async method that is genuinely off the hot path]** → Medium confidence and suppressible; the common accidental case is worth surfacing.
- **[Count-for-existence overlap with EFD002]** → A synchronous `Count()` used for existence in an async method can draw both an EFD002 (use `Any`) and an EFD010 (use `CountAsync`/`AnyAsync`) finding; both are correct and distinct, and the overlap is rare. Not specially de-duplicated.
- **[Async lambdas vs. methods]** → Handled by checking the nearest enclosing function's `IsAsync`, so async lambdas are covered and sync lambdas inside async methods are not.

## Migration Plan

1. Add `SyncDatabaseCallInAsyncAnalyzer` (EFD010) with the async-context check and counterpart mapping.
2. Register it in `WorkspaceAnalyzer` and add the `AnalyzerReleases.Unshipped.md` entry.
3. Add analyzer fixtures/tests and a buildable CLI fixture with end-to-end coverage.
4. Add `docs/rules/EFD010.md`, the README rule-table row, and a CHANGELOG entry.
5. Sync the new `efd010-sync-db-call-in-async` spec and archive.

No persisted data or schema change. Rollback removes the analyzer and its registration.
