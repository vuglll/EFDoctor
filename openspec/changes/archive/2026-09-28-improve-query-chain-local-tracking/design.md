# Design

## Context

`EfQueryOperationAnalysis.TryAnalyzeSourceCore` recurses from a terminal operator down through resolved `Queryable` and EF composition, and stops at a `DbSet` or `DbContext.Set<T>()`. Any other operation, including a local reference, fails the proof. EFD006, EFD017, and EFD025 contain their own copies of that recursion so they can collect `Include` state. EFD020 already tracks `IQueryable` locals for its own purpose (counting executions), but it skips reassigned locals and proves only the initializer.

## Goals and non-goals

- **Goal:** follow the common straight-line shape, `var q = <query>; [q = q.<composition>;]* … q.<terminal>`, in every rule that proves a chain.
- **Non-goal:** general data flow. Conditional composition, loop-carried values, fields, properties, parameters, returns, and helper methods are out of scope.

## Decisions

### Only statically determined values are followed

**Decision:** `EfQueryLocals.TryResolve(ILocalReferenceOperation read, out IOperation value)` succeeds only when:

1. The local has exactly one declarator with an initializer, and it isn't a `ref` local. Its declaration statement is a direct child of an `IBlockOperation`, B.
2. Every other reference to the local anywhere in the body, including lambdas and local functions, is one of two things:
   - a plain read;
   - the target of an `ISimpleAssignmentOperation` (not `ref`) whose parent is an `IExpressionStatementOperation` that is a direct child of B.

   Any other write disqualifies the local: compound, coalescing, increment, deconstruction, a `ref`/`out`/`in` argument, a `ref` local initializer, or `&`.
3. Walking up from the read, the first ancestor that is a direct child of B is some statement S, and the walk crosses no `IAnonymousFunctionOperation` or `ILocalFunctionOperation`. S comes after the declaration statement.
4. The value is the assignment value of the latest write at an index before S, or else the initializer.

**Why:**
- With every write at the top level of one block, the value at any later statement of that block is the latest earlier write, whatever path led there. The declaration re-runs if B re-runs, so loop-carried values can't reach the read.
- A read in a nested lambda may run after later writes, so it isn't followed.

**Alternatives:**
- Reaching definitions over the control-flow graph, merging conditional writes. Rejected for now: rules that report an absence (EFD005's bound, EFD014's ordering) would need a "may" vs "must" split. Without it, they would report queries that are bounded or ordered on every path through an `if`/`else`.
- Following locals only in origin-only rules. Rejected: it splits the proof in two and invites rules to use the wrong one.

### One cache per operation tree

**Decision:** A `ConditionalWeakTable<IOperation, BodyLocals>`, keyed by the root of the operation tree, maps each local symbol to its declaration, its writes, and whether it is followable. Each root is built once, with a single pass over its descendants.

**Why:** The same body is visited by many operation callbacks and rules. The table is thread-safe and doesn't keep trees alive.

### Hop limit

**Decision:** At most 8 local hops per proof.

**Why:** It bounds the work on pathological code. The index rule already prevents cycles, because each hop moves strictly earlier in a block or into an enclosing block.

### Evidence names the local

**Decision:** When the proof passes through a local, it appends `local '<name>'` to `EfQueryChainAnalysis.Operations`, after the operations of the local's value.

**Why:** EFD005 and EFD014 print the chain from `Operations`, so their evidence then reads, for example, `Queryable.Where -> local 'query' -> Queryable.OrderBy`. No rule matches on that entry.

### Include findings stay unique across a local

**Decision:** The include walkers mark each include they collect through a followed local's value as `ThroughLocal`.
- **EFD006** reports at the second distinct collection include. If that include is `ThroughLocal`, the first one is too, because includes are in source order, so the chain that ends at the local's value already reports it. The later chain end skips it.
- **EFD025**, for an include C that is `ThroughLocal`:
  - It skips C when C is redundant among the `ThroughLocal` includes alone. The local's value already reports that.
  - It reports C when only a later include makes C redundant. That's a longer path that covers it.
  - A duplicate always has its earlier twin at or before C, so for a `ThroughLocal` C the value's own chain reports it.

**Why:** Both rules report at an include's location, so a second report would duplicate the finding at the same span.

**Limitation:** Two different later chain ends that each add an include covering the same `ThroughLocal` include report it twice at the same span. The CLI deduplicates by location. In an IDE, the two diagnostics would stack.

### Existing split-mode behavior is unchanged

A chain end that adds `AsSplitQuery` after a local doesn't stop the local's own value from being reported by EFD006. That matches today's per-expression behavior. A local value with `AsSplitQuery` does silence later chain ends, because the split mode is part of the followed value.
