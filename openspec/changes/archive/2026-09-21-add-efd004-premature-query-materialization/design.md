# Design

## Context

See [proposal.md](proposal.md) for motivation and [the EFD004 specification](specs/efd004-premature-query-materialization/spec.md) for observable behavior.

EFDoctor currently registers independent Roslyn operation analyzers and maps their diagnostics into a shared finding contract. EFD002 already demonstrates how to trace an inline query chain back to `DbSet`, but EFD004 must additionally classify a materializer, walk outward to its immediate consumer, and reject downstream expressions that may require client evaluation.

EF Core translates query composition before terminal execution. Once `ToList`, `ToArray`, or an asynchronous counterpart completes, later `Enumerable` operations execute over client data. Microsoft guidance distinguishes explicit client evaluation and buffering from server composition and notes that `ToList`/`ToArray` buffer results. The analyzer cannot ask a provider whether every arbitrary expression is translatable, so the design uses a deliberately narrow semantic subset.

## Goals / Non-Goals

**Goals:**

- Prove that a supported materializer's inline source originates from `DbSet`.
- Recognize the immediate LINQ-to-Objects operation after materialization, including an intervening `await`, parentheses, or implicit conversions.
- Classify only simple downstream arguments with predictable SQL equivalents.
- Keep each analysis stateless, concurrent-safe, deterministic, and compatible with the analyzer project's `netstandard2.0` target.
- Reuse the existing diagnostic properties and CLI reporting pipeline unchanged.

**Non-Goals:**

- Ask an EF provider to translate an expression or execute target code.
- Perform local, field, property, interprocedural, or general data-flow tracking.
- Detect every possible provider-translatable method, computed property, custom converter, or user-defined expression.
- Diagnose unbounded terminal materialization without later composition; that remains distinct from EFD004.
- Generate a code fix or automatically rewrite query semantics.

## Decisions

### 1. Analyze supported materializer invocations and walk one consumer edge outward

The analyzer will register an invocation operation action after resolving `Enumerable`, `Queryable`, `DbSet<T>`, and `EntityFrameworkQueryableExtensions` at compilation start. It will classify:

- `Enumerable.ToList` and `Enumerable.ToArray` as synchronous materializers; and
- EF Core `ToListAsync` and `ToArrayAsync` as asynchronous materializers.

For an eligible materializer, it will walk through transparent conversions, parentheses, and one `await`, then require the resulting operation to be the source argument of the immediate outer `Enumerable` invocation. Only `Where`, `Select`, `OrderBy`, `OrderByDescending`, `Skip`, and `Take` proceed to argument classification.

This naturally produces one diagnostic per materializer even when additional operators follow. It also prevents the analyzer from skipping an unsupported client operation to reach a later supported one.

Alternatives considered:

- Starting from every downstream LINQ operator makes de-duplication harder and can accidentally jump across unsupported operations.
- Syntax-chain matching is simpler but cannot distinguish EF execution, LINQ-to-Objects, unrelated methods, or static-call forms reliably.

### 2. Prove EF query origin within the same operation tree

A dedicated query-source classifier will unwrap transparent operations and recursively follow only resolved `Queryable` methods and EF Core query-composition extensions until it reaches `DbSet<T>` or `DbContext.Set<T>()`. It will reject arbitrary `IQueryable<T>` parameters and stop at `Enumerable.AsEnumerable`, EF `AsAsyncEnumerable`, or any other client boundary.

Both synchronous and asynchronous materializers require this proof. This is intentionally stricter than treating any EF-named asynchronous extension call as sufficient evidence.

Alternative considered: accepting every `IQueryable<T>` would report custom providers and queries whose EF origin is not established, conflicting with the high-confidence target.

### 3. Use operator-specific conservative argument classifiers

The downstream invocation's non-source arguments will be classified by operation shape:

- `Where`: a single-parameter lambda composed from entity property paths, scalar constants/captured values, null patterns, built-in comparisons, and built-in boolean operations.
- `Select`: a single-parameter lambda returning a property path or anonymous object whose values are supported property/scalar expressions.
- `OrderBy`/`OrderByDescending`: a single-parameter lambda returning a direct property path.
- `Skip`/`Take`: an integral constant, method parameter, or captured local reference.

The classifier will reject indexed overloads, dynamic operations, assignments, arbitrary invocations, user-defined operators, and expression nodes outside the allowlist. Property access must be rooted in the lambda parameter for entity values; captured object-property traversal is not accepted as a scalar captured value.

Classifier helpers will return a reason/category used in test diagnostics but will not claim provider-level proof. The public message remains qualified because mappings and provider capabilities can still differ.

Alternatives considered:

- Allowing common string/date methods would increase coverage but requires a provider- and version-sensitive translation matrix.
- Rejecting all lambdas except property equality would miss straightforward boolean combinations and projections without materially improving certainty.

### 4. Locate and describe the materializer, not the outer chain

The diagnostic location will span only the complete `ToList`, `ToArray`, `ToListAsync`, or `ToArrayAsync` invocation. Evidence will include the resolved materializer, the traced `DbSet`-origin expression, and the immediate supported `Enumerable` operator. This keeps coordinates stable and tells the developer exactly which execution boundary to move.

Impact and remediation will vary by operator category:

- filtering/paging: excess rows may be transferred and buffered;
- projection: excess columns or entity materialization/tracking may occur; and
- ordering: sorting happens over buffered client data.

All variants will recommend moving composition before materialization only after preserving semantics and checking generated SQL. Legitimate client evaluation and small/bounded data remain documented exceptions.

### 5. Keep EFD004 independent while sharing narrow utility code only where safe

EFD004 may introduce internal transparent-operation and query-source helpers, but it will not change EFD002 behavior as part of this change. A later refactor can consolidate duplicate logic once both rule suites prove equivalent behavior. This avoids widening EFD002's existing contract accidentally.

The CLI integration is a single analyzer registration. Existing mapper, ordering, de-duplication, exit codes, console renderer, and JSON schema remain untouched.

### 6. Verify with focused expression fixtures and a dedicated CLI project

Analyzer tests will provide more than ten positive and ten negative cases across all four materializers and all downstream categories. They will explicitly cover static and extension syntax, async `await`, composed query origins, indexed-lambda rejection, custom methods, explicit client boundaries, arbitrary `IQueryable`, stored locals, unrelated methods, multiple diagnostics, exact locations, and standard suppression.

A dedicated EFD004 fixture project will contain unsuppressed and suppressed examples without changing the finding counts of existing fixtures. One end-to-end test will run console and JSON scans and assert rule metadata, actionable evidence, exact locations, schema version, deterministic output, and exit code.

## Risks / Trade-offs

- **[Provider translation varies]** Even simple property expressions can be affected by mappings or provider behavior. → Keep language qualified, use a narrow allowlist, require SQL Server/EF semantics through the query origin, and instruct users to verify generated SQL.
- **[False negatives from expression-local scope]** A list stored in a local and filtered on the next line remains undetected. → Document the boundary and defer data flow until precision can be measured independently.
- **[False negatives for translatable methods]** Common string/date operations are excluded initially. → Prefer silence to a client-evaluation false positive and add methods later with explicit provider/version fixtures.
- **[Projection may alter tracking semantics]** Moving `Select` before materialization can stop entity materialization or tracking. → Explain this effect and require semantic review instead of presenting an automatic rewrite.
- **[Operation-tree shape differences]** Extension syntax, static syntax, implicit arrays, and awaited calls produce different parent shapes. → Centralize transparent-parent traversal and cover each shape with focused tests.
- **[Overlap with future unbounded-query rule]** EFD004 and a later rule may target the same materializer for different reasons. → Keep EFD004 evidence tied to the immediate downstream operation and rely on rule ID ordering/de-duplication rather than merging concerns.

## Migration Plan

1. Add the analyzer and focused tests without changing shared report models.
2. Register EFD004 in the CLI and add its isolated fixture/end-to-end assertions.
3. Add rule documentation and update README status, boundaries, suppression, and verification maps.
4. Run a clean build and the full regression suite, then validate the OpenSpec change.

Rollback is additive: remove EFD004 from the CLI registry and revert its analyzer, tests, fixture, documentation, and release note. No persisted data or external schema migration is involved.
