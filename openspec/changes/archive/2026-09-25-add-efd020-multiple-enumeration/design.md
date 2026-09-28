# Design

## Context

See `proposal.md` for motivation. EFDoctor analyzers resolve EF Core markers in a compilation-start action. `MissingForeignKeyIndexAnalyzer` already uses `RegisterOperationBlockAction` and iterates `context.OperationBlocks`, which is the pattern EFD020 follows to reason about a whole method body. `EfQueryOperationAnalysis` exposes public `TryAnalyzeSource` (traces an operation's inline source to a `DbSet` origin), `NormalizeMethod`, and `Unwrap`, all reused unchanged so EFD020 stays isolated from parallel query-rule work.

## Goals / Non-Goals

**Goals:**

- Detect an EF `IQueryable` local executed 2+ times in one method, with acceptable (medium) false-positive risk.
- Anchor on the query declaration and report once per over-enumerated local.
- Reuse the shared tracer; no edits to shared analysis code.

**Non-Goals:**

- Cross-method flow: an `IQueryable` returned from, or passed into, another method and enumerated there. Method-local scope keeps execution counting sound and false positives low.
- Counting executions of a *composed* query (`var q2 = q.Where(...)`) as executions of `q`; only direct executions of the local are counted.
- Proving the two executions run at runtime (both branches of an `if`/`else` still count as reuse of the same query variable; accepted).

## Decisions

### 1. Method-body block analysis

Use `RegisterOperationBlockAction`. Across the method's operation blocks, collect candidate locals: an `IVariableDeclaratorOperation` whose symbol's type implements `System.Linq.IQueryable<T>` and whose initializer, after `Unwrap`, satisfies `TryAnalyzeSource` (proven `DbSet` origin). Then walk the blocks again, counting executions per candidate local. Report each candidate with an execution count ≥ 2, anchored on its declaration.

`TryAnalyzeSource` on the initializer is the key filter: it is true only for a deferred query that traces to a `DbSet`, which excludes already-materialized results (`ToList()` returns `List<T>`, not an `IQueryable`, and does not trace) and arbitrary/in-memory sources.

### 2. What counts as an execution

An execution of local `q` is:

- an `IForEachLoopOperation` whose collection, unwrapped, is a reference to `q`; or
- an `IInvocationOperation` whose source (`GetInvocationSource`, unwrapped) is a reference to `q` and whose resolved method name is a known materializing/aggregating terminal — `ToList`/`ToArray`/`ToDictionary`/`ToHashSet`, `Count`/`LongCount`, `Any`/`All`, `First`/`FirstOrDefault`/`Single`/`SingleOrDefault`/`Last`/`LastOrDefault`, `Min`/`Max`/`Sum`/`Average`, `Contains`, `ElementAt`, and their `Async` variants.

Non-terminal composition (`Where`, `Select`, `OrderBy`, …) is not an execution; it produces a new query. `AsEnumerable`/`AsQueryable` are boundaries, not executions.

### 3. Conservative exclusions and confidence

A local that is reassigned after declaration (any assignment operation targets it) is dropped, because later executions may run a different query. Confidence is `medium`: re-executing a query is usually a bug, but an intentional re-query (to observe changed data across a save) is legitimate, so the rule downgrades rather than over-claims. The central test-project downgrade still applies.

## Risks / Trade-offs

- **[Intentional re-query flagged]** → Medium confidence and suppressible; the common accidental case is worth surfacing.
- **[Executions in mutually exclusive branches counted as reuse]** → Accepted; even one re-execution path is a latent extra round trip, and precise path analysis is out of scope.
- **[Cross-method enumeration missed]** → Documented limitation; method-local scope keeps the rule sound and low-FP.

## Migration Plan

1. Add `MultipleEnumerationAnalyzer` (EFD020) with a block action.
2. Register it in `WorkspaceAnalyzer` and add the `AnalyzerReleases.Unshipped.md` entry.
3. Add analyzer fixtures/tests and a buildable CLI fixture with end-to-end coverage.
4. Add `docs/rules/EFD020.md`, the README rule-table row, and a CHANGELOG entry.
5. Sync the new `efd020-multiple-enumeration` spec and archive.

No persisted data or schema change. Rollback removes the analyzer and its registration.
