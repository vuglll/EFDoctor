# Design

## Context

See `proposal.md` for motivation. EFDoctor analyzers register an `OperationKind.Invocation` action and use `EfQueryOperationAnalysis.TryAnalyzeSource` to trace an invocation's inline source back to a `DbSet`/`DbContext.Set<T>()` origin, returning an `EfQueryChainAnalysis` with the proven `Origin` and an ordered `Operations` list (entries such as `Queryable.Where`, `Queryable.OrderBy`, `Queryable.Skip`). Tracing is inline-only and stops at stored `IQueryable` locals and client-evaluation boundaries. `Skip`, `Take`, and the ordering operators are all `System.Linq.Queryable` methods, so they already appear in `Operations`.

## Goals / Non-Goals

**Goals:**

- Detect `Skip`/`Take` paging with no preceding ordering over a proven EF query, with low false positives.
- One finding per paged query (anchor on the outer paging operator).
- Reuse the shared tracer; no new query-analysis machinery.

**Non-Goals:**

- Judging whether an existing `OrderBy` key is actually unique/stable — presence of any ordering operator before paging suppresses the finding.
- Following stored `IQueryable` values or crossing client-evaluation boundaries (consistent inline-only limitation).
- Flagging in-memory (`IEnumerable`) paging; only proven database queries are in scope.

## Decisions

### 1. Anchor on the paging operator and trace its own chain

Register on `OperationKind.Invocation`. When the resolved target is `System.Linq.Queryable.Skip` or `.Take`, call `TryAnalyzeSource` on that invocation. Because the tracer records operators source-first and only up to the traced invocation, the returned `Operations` contains exactly the operators that precede (and include) this paging operator. Report when tracing succeeds (origin is a `DbSet`) and `Operations` contains no ordering operator (`OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending`).

Tracing from the paging operator itself naturally enforces "ordering must come before paging": an ordering applied *after* `Skip`/`Take` (which does not fix determinism) is not in the traced prefix and so does not suppress.

Alternative considered: analyze at the terminal materializer and inspect the whole chain. Rejected: paging can be materialized in many ways (or returned as `IQueryable`), and anchoring at the operator gives a precise diagnostic location and works even without an inline materializer.

### 2. Single finding via outermost-operator dedup

`Skip(n).Take(m)` registers two invocations. To emit one finding, skip reporting when the current paging operator is the immediate `Queryable` source of an enclosing `Skip`/`Take` — i.e., report only the outermost paging operator. Implemented by walking up from the invocation to the first enclosing `IInvocationOperation` and checking whether it is a `Queryable` `Skip`/`Take` whose traced source is the current invocation. `OrderBy(...).Skip(n)` still reports on `Skip` (its consumer, if any, is a materializer, not a paging op) unless the ordering already suppresses it.

### 3. Require Skip; do not report bare Take

Pagination requires `Skip`: skipping rows is only meaningful over a defined order, so `Skip` without ordering is the unambiguous non-deterministic-paging bug and is reported at `high` confidence. A bare `Take` (top-N) without ordering is frequently intentional ("give me any N", batch reads), so EFD014 does not report it at all. This decision was validated against the existing EFD013 and EFD005 test fixtures, whose `Where(...).Take(n)` batch reads would otherwise produce noise — exactly the kind of low-value finding the catalog's "Low FP" rating and the project's trust-first philosophy warn against. Confidence flows through the existing confidence-driven severity mapping and the central test-project downgrade.

## Risks / Trade-offs

- **[A genuinely wrong bare `Take` without order is missed]** → Accepted: requiring `Skip` trades that rarer, ambiguous case for near-zero false positives on the very common intentional top-N/batch shape, preserving trust.
- **[An ordering operator hidden behind a stored local or helper is missed]** → Consistent inline-only limitation shared by other rules; a missed ordering yields a finding the developer can suppress, and cross-statement composition is out of scope by design.
- **[Ordering via raw SQL or a custom extension is not recognized]** → Accepted; only resolved `Queryable` ordering counts, matching the conservative, symbol-based approach of the other rules.

## Migration Plan

1. Add `UnorderedPaginationAnalyzer` (EFD014) reusing `EfQueryOperationAnalysis`.
2. Register it in `WorkspaceAnalyzer`.
3. Add analyzer fixtures/tests and a buildable CLI fixture with end-to-end coverage.
4. Add `docs/rules/EFD014.md` and update the README rule table.
5. Sync the new `efd014-unordered-pagination` spec and archive.

No persisted data or schema change. Rollback removes the analyzer and its registration.
