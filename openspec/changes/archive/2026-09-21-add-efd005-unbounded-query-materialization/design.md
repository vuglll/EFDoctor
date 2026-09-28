# Design

## Context

See [proposal.md](proposal.md) for motivation and [the EFD005 specification](specs/efd005-unbounded-query-materialization/spec.md) for observable behavior.

EFDoctor currently runs independent Roslyn operation analyzers and maps diagnostic properties into one shared console/JSON finding contract. EFD004 already proves inline EF query origin and classifies materialization followed by SQL-capable client composition. EFD005 needs the same origin proof, but it reasons backward through the query chain to decide whether a server-side row limit exists and must avoid producing a second finding where EFD004 already gives the more specific diagnosis.

The product brief explicitly assigns EFD005 medium-high false-positive risk. Static analysis can prove that no recognized `Take` exists in an inline expression, but it cannot prove table cardinality, business intent, selectivity, production workload, or whether complete enumeration is required. The diagnostic therefore uses medium confidence and qualified remediation.

## Goals / Non-Goals

**Goals:**

- Prove supported `ToList` and `ToListAsync` calls originate from an inline EF Core `DbSet` query.
- Inspect the complete supported query chain for a resolved `Queryable.Take` row bound.
- Explain the exact observed chain and the limits of the conclusion.
- Avoid duplicate EFD004/EFD005 diagnostics at one materializer.
- Keep analysis stateless, concurrent-safe, deterministic, local-only, and compatible with the analyzer project's `netstandard2.0` target.

**Non-Goals:**

- Estimate row counts, filter selectivity, database size, query cost, or runtime memory use.
- Decide whether a `Take` count is sufficiently small or appropriate.
- Infer uniqueness from predicates, keys, indexes, or model metadata.
- Follow `IQueryable` values through variables, members, arguments, helpers, or interprocedural data flow.
- Diagnose `ToArray`, dictionaries, lookups, streaming enumeration, or single-result/aggregate terminal operators in this rule.
- Add a code fix or insert an arbitrary row limit automatically.

## Decisions

### 1. Analyze supported materializers and recursively summarize their inline source chain

The analyzer will register for invocation operations after resolving `Enumerable`, `Queryable`, `DbSet<T>`, `DbContext`, and `EntityFrameworkQueryableExtensions`. It will classify only unreduced `Enumerable.ToList` and EF Core `ToListAsync` overloads. Static and reduced extension syntax use the same normalized method-symbol checks.

Starting from the materializer source, a recursive classifier will unwrap parentheses and implicit conversions, follow only resolved `Queryable` and EF composition calls, and stop successfully at `DbSet<T>` or `DbContext.Set<T>()`. Along the way it will collect normalized operation names and whether `Queryable.Take` occurred. Encountering `Enumerable`, `AsAsyncEnumerable`, an unrelated extension, an arbitrary `IQueryable`, or an unresolved operation rejects origin proof.

Alternative considered: treating all `IQueryable<T>` sources as database queries. This would include non-EF providers and query variables whose provider cannot be established, conflicting with the rule's semantic-evidence requirement.

### 2. Treat semantic Queryable.Take presence as the first-version definition of bounded

Any resolved `Queryable.Take` in the proven query chain counts as an explicit bound, regardless of whether its count is a literal, parameter, or computed expression. EFD005 is responsible for detecting the absence of a limit, not judging the limit's magnitude or validity. A negative or zero count is still bounded by LINQ semantics.

`Where`, `Distinct`, `Skip`, `Select`, ordering, grouping, `Include`, and tracking modifiers do not establish a maximum result count. Equality against an apparent identifier is not treated as bounded because uniqueness cannot be proven reliably from the expression alone.

Alternatives considered:

- Requiring a small constant `Take` would impose an arbitrary product threshold and report legitimate parameterized pagination.
- Treating key-looking predicates as bounds would require reliable model/key analysis across ordinary application code and would still confuse expected selectivity with a guaranteed maximum.
- Configuration for a maximum row threshold is deferred because the first version has no runtime row estimate to compare with it.

### 3. Share narrow semantic utilities with EFD004 to keep overlap decisions exact

The implementation will extract narrowly scoped internal helpers for method normalization, transparent-operation unwrapping, query-source access, EF-origin tracing, and EFD004 immediate-consumer eligibility. EFD004 will be regression-tested unchanged after the extraction. EFD005 will call the same eligibility helper and suppress itself only when EFD004 would report the identical materializer.

If the immediate post-materialization operation is unsupported by EFD004—for example, a custom client predicate—EFD005 remains eligible because there is no duplicate specific finding. This preserves coverage without emitting two messages at one location.

Alternative considered: suppressing EFD005 for every post-materialization operation. That would create a blind spot whenever EFD004 correctly rejects an expression as not conservatively SQL-capable.

### 4. Keep the finding qualified and locate the execution boundary

The diagnostic location will span the complete `ToList` or `ToListAsync` invocation. Evidence will include the resolved materializer, the traced `DbSet` origin, a deterministic summary of observed query operations, and that no `Queryable.Take` was found.

The descriptor will use title `Query materialization has no recognized row bound`, warning severity, medium confidence, and documentation key `EFD005`. Impact language will say the query may transfer and buffer more rows than expected and may increase database work, memory use, and latency. It will not claim the result is large, incorrect, or measured.

Remediation will present choices rather than an automatic rewrite: tighten filtering when semantically valid, page with stable ordering and `Take`, stream or process in chunks, or use a purpose-built aggregate/terminal operation. It will explicitly warn against adding an arbitrary limit that discards required results and acknowledge legitimate complete-result workloads.

### 5. Preserve existing reporting and suppression infrastructure

EFD005 will add one analyzer registration and one unshipped release-note row. Existing finding mapping, deterministic sorting, de-duplication, console and JSON rendering, schema version `1`, exit codes, and standard Roslyn suppression remain unchanged. No analyzer configuration option or proprietary suppression file is introduced.

### 6. Use isolated analyzer and CLI fixtures

Analyzer tests will contain more than ten positive and ten negative cases. Positive cases will cover direct and composed sources, sync/async and static syntax, filters and projections without bounds, `Skip`, `Include`, tracking modifiers, task-returning async calls, and multiple materializers. Negative cases will cover constant/parameter/composed `Take`, in-memory and arbitrary-queryable sources, query locals, client boundaries, unrelated/unresolved methods, `ToArray`, generated code, and EFD004 overlap.

A dedicated EFD005 fixture project will contain unsuppressed findings and pragma, editor-config, and `SuppressMessage` cases without changing existing fixture counts. End-to-end tests will verify console and JSON content, medium confidence, exact coordinates, deterministic order, schema version, suppression, and exit code.

## Risks / Trade-offs

- **[Legitimate full-result operations remain common]** → Use medium confidence, qualified wording, explicit exception guidance, and easy standard suppression with justification.
- **[Take does not guarantee a sensible limit]** → Describe it only as a recognized explicit bound; do not claim the bounded query is efficient or safe.
- **[Selective predicates may be practically bounded]** → Avoid pretending static syntax proves cardinality; document known-small and business-constrained datasets as review/suppression cases.
- **[Expression-local tracing creates false negatives]** → Document that query locals and helper-returned queries are outside the first version; add value-flow only as a separately measured change.
- **[Shared-helper extraction could regress EFD004]** → Preserve EFD004's existing public behavior and run its focused suite before enabling EFD005.
- **[Overlap with future materialize-then-reduce EFD019]** → EFD005's evidence remains about absence of a query-side bound; a future change must define precedence for EFD019 before registration.
- **[README currently assigns different behavior to EFD005]** → Update the README catalog and rule-status sections to match the newer product brief as part of this change.

## Migration Plan

1. Extract and regression-test the shared semantic query helpers without changing EFD004 results.
2. Add EFD005 with focused analyzer fixtures and release tracking.
3. Register the analyzer, add its isolated CLI fixture, and verify end-to-end reporting and suppression.
4. Add rule documentation, correct the README catalog, and run the clean full regression and strict OpenSpec validation.

Rollback is additive: remove EFD005 registration and its analyzer, tests, fixture, documentation, release row, and shared-helper changes; restore the private EFD004 helpers if the extracted utility is not otherwise used. No persisted data or external schema migration is involved.
