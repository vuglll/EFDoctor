# Design

## Context

`EfQueryLocals` builds one cache per operation tree. It records, for each local, its declaration and writes, and decides whether the local is straight-line followable. `EfQueryOperationAnalysis.TryAnalyzeSourceCore` asks it to follow a local and otherwise fails. Rules call `TryAnalyzeSource` directly, except EFD009 and EFD023, which go through `EfPredicateScan`. See proposal.md for motivation.

## Goals / Non-Goals

**Goals:**
- Prove the origin of the `if (x) query = query.Where(…)` shape, and its loop and conditional-expression variants, for the seven rules that need only the origin.
- Leave the full chain proof, and every rule that doesn't opt in, unchanged.

**Non-Goals:**
- Reaching definitions or path-sensitive composition, which would need a "may" vs "must" split for EFD005 and EFD014.
- Locals declared without an initializer and assigned on every branch.
- Replacing a query from another source.
- Fields, parameters, and helper methods.

## Decisions

### Self-composition keeps the origin invariant

**Decision:** A local qualifies when every write after the initializer is `local = <compose>(local)`, with only element-preserving `Queryable` operators and bound-neutral EF methods between the read and the write.

**Why:** Each write then produces a query with the initializer's origin and element type, whatever ran before it. By induction, every read of the local, anywhere in the body, has that origin. No position or control-flow reasoning is needed, so reads inside branches, loops, and lambdas are all covered.

**Alternatives considered:**
- Allowing any write whose value is a proven EF query from the same `DbSet`. Rejected: it needs an origin-equality notion, such as the same context instance or the same set, that rules like EFD013 rely on, for little extra recall.
- A full reaching-definitions pass over the control-flow graph. Rejected for the reason in Non-Goals.

### Opt-in, not a default

**Decision:** `TryAnalyzeSource` takes an optional `followComposedLocals` flag, false by default. `EfPredicateScan.Create` takes the same flag. The seven rules pass `true`.

**Why:** A rule that reports an absence (EFD005's bound, EFD014's ordering, EFD037's include) would report a false positive when a branch adds the missing operation. Opting in keeps the default safe, and makes each rule's choice visible at its call site.

### The operation list holds only operations that apply on every path

**Decision:** The hop appends `local '<name>' (conditionally composed)` after the initializer's operations. It adds nothing from the conditional writes. The row bound is the initializer's.

**Why:** Operations in the conditional writes may not apply on a given path. Leaving them out keeps every consumer of `Operations` sound. For example, EFD019's `IsOrdered` stays a must-check without any change.

### Collection reuses the existing cache

**Decision:** `LocalWrites` keeps two flags.
- **Unsafe:** a `ref`/`out`/`in` argument, a `ref` alias, an address-of, a deconstruction, a compound, coalescing, or increment write, a declaration pattern, or an assignment used as an expression. Unsafe disqualifies both proofs.
- **Not straight-line:** a write outside the declaring block, or a declaration not directly in a block. This disqualifies only the full proof.

The collector also keeps every plain assignment's value, for the self-composition check. The check itself lives in `EfQueryOperationAnalysis`, which has the resolved `Queryable` and EF symbols, and its result is computed once per local.

**Why:** It is one pass and one cache, as today. Keeping the symbol-dependent check out of `EfQueryLocals` keeps that helper symbol-free.

### Hop limit is shared

The origin hop counts against the same 8-hop limit, and the initializer is proven with `localHops + 1`. An initializer can't read its own local, and each hop moves to an earlier declaration, so cycles are impossible.

## Risks / Trade-offs

- **[Risk] A conditional write inside a lambda runs later, on another thread or after disposal.** → The origin is unaffected: the same `DbSet` expression from the same captured context. Timing issues belong to EFD027, which doesn't opt in.
- **[Risk] Opted-in rules may have a hidden dependence on `Operations`.** → Audit each of the seven call sites. EFD019's `IsOrdered` is the only `Operations` consumer among them, and it stays sound because conditional operations are excluded.
- **[Risk] New findings in a pattern that is noisy in practice.** → The corpus re-run is a release gate. Triage every new finding, and fix any false-positive class before shipping.
- **[Trade-off] Evidence for EFD004 and EFD019 now names an origin whose composition is only partly known.** → The `(conditionally composed)` marker in the chain says so.
