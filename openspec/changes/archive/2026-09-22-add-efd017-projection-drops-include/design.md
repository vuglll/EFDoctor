# Design

## Context

EFDoctor's query-shape analyzers start from Roslyn invocation operations, normalize reduced extension methods, prove `DbSet<T>` or `DbContext.Set<T>()` origins, and attach a shared diagnostic-property contract consumed unchanged by the CLI. `MultipleCollectionIncludeAnalyzer` already walks inline EF query composition and resolves strongly typed collection include paths, while `EfQueryOperationAnalysis` provides common symbol normalization, source extraction, lambda recovery, and operation unwrapping. See `proposal.md` for motivation and the EFD017 and CLI deltas for observable behavior.

EFD017 needs to distinguish a projection that returns only a new scalar/value shape from one that still carries an entity-bearing value. Roslyn provides source-language types and operation structure but not the target application's runtime EF model, so the safe boundary is to classify only definitely non-entity projections and remain silent on ambiguous reference or collection leaves.

## Goals / Non-Goals

**Goals:**

- Prove the EF include, LINQ projection, and database-query origin from resolved symbols.
- Produce one stable finding per eligible projection even when several include paths precede it.
- Classify common scalar, anonymous-object, tuple, and DTO construction shapes without loading or executing the target application.
- Reuse the existing reporting, suppression, CLI, and test-project policy unchanged.

**Non-Goals:**

- Runtime EF model construction, provider SQL inspection, or query execution.
- General data-flow through locals, members, parameters, helpers, or custom query operators.
- Determining whether an arbitrary reference-typed projection leaf is an EF entity.
- Diagnosing `Include` applied after a projection, auto-includes, explicit loading, lazy loading, or in-memory `Enumerable.Select`.
- A code fix or automatic rewrite of the query.

## Decisions

### Analyze the outermost eligible `Queryable.Select`

Register a compilation-start invocation operation action after resolving `Queryable`, `DbSet<T>`, `DbContext`, and `EntityFrameworkQueryableExtensions`. Continue only when the normalized target is `Queryable.Select` with a single-parameter selector. Analyze the source chain from that boundary and emit at most one EFD017 diagnostic for it.

Starting at `Select` makes the exact point where include semantics change available and prevents one diagnostic per earlier `Include`. Starting at each include and searching parents was rejected because it complicates deduplication and can miss static-call or wrapped composition forms.

### Walk only a directly connected supported query chain

From the `Select` source, recursively unwrap parentheses and implicit conversions, then follow source arguments through normalized `Queryable` calls and EF query-composition calls that preserve the query source. Stop successfully at a `DbSet<T>` or `DbContext.Set<T>()` origin. Record resolved `Include` and `ThenInclude` operations and allow intervening filters, ordering, paging, tracking modes, split/single-query modes, and tags. Reject `Enumerable` operators, materializers, unknown extension methods, local/member references, and any chain whose origin cannot be proven.

This follows the existing EFD006 inline-chain precedent. Reaching-definition analysis was considered, but deferring locals keeps the initial evidence deterministic and avoids stale include state across branches or reassignments.

### Recover strongly typed include paths conservatively

For expression-based includes, inspect the resolved selector operation and record source-rooted property paths, including filtered-include composition and subsequent `ThenInclude` segments. Preserve the invocation text as fallback evidence if a complete property path cannot be represented, but require at least one semantically resolved include before reporting. String-based includes are deferred because validating model paths would require runtime EF metadata and plain string evidence would weaken the semantic contract.

The existing EFD006 include walker can be factored into a small shared helper if that reduces duplication without changing EFD006 behavior. Copying a minimal EFD017-specific walker remains acceptable if sharing would couple collection-cardinality logic to projection analysis.

### Classify only definitely non-entity projection results

Recover the selector's returned expression from expression-bodied and single-return block lambdas. Recursively classify the outward result shape:

- scalar, enum, nullable value, string, and other value-type leaves are definitely non-entity;
- object creation, anonymous-object creation, tuple creation, and member initialization are eligible only when every assigned/projected leaf is definitely non-entity;
- the selector parameter itself is entity-preserving and rejects the diagnostic;
- a direct source-rooted property, field, conversion, conditional branch, collection, or other reference-typed leaf is ambiguous and rejects the diagnostic unless it is `string`;
- invocations and nested query expressions are eligible only when their outward result is a scalar/value shape and they do not return or embed an ambiguous reference value.

This deliberately permits `new Dto { Id = entity.Id, OwnerName = entity.Owner.Name }`, because the projected leaves are scalar and the `Include` is irrelevant to selecting those columns, while remaining silent on `new { Entity = entity }`, `entity.Owner`, or `new { entity.Children }`. Attempting to infer EF entity types from naming, `DbSet` membership, or class shape was rejected as too fragile without model metadata.

### Report at the projection boundary with explicit alternatives

Place the diagnostic on the complete `Select` invocation that first creates the definitely non-entity result. Evidence names the proven query origin, resolved include path or paths, and selector result type/shape. Use warning severity and high confidence. Explain that the projection itself determines selected data and the preceding include does not populate navigation state outside that projected shape.

Remediation offers two branches: remove the redundant include and explicitly project every required value, or return/materialize the entity-bearing query when the caller genuinely requires populated navigation state. It must not recommend materializing before `Select` as a default fix, because that can introduce the over-fetching already covered by EFD004.

### Keep CLI integration and policies centralized

Register the analyzer once in `WorkspaceAnalyzer`. Existing mapping, deterministic ordering, console/JSON rendering, exit codes, privacy behavior, standard Roslyn suppression, and uniform test-project downgrade remain unchanged. A buildable EFD017 fixture will cover positive, excluded, and suppressed shapes at process level.

## Risks / Trade-offs

- [Ambiguous entity-bearing projections are missed] → Remain silent unless every outward projection leaf is definitely non-entity; expand only when model-aware evidence justifies it.
- [EF Core query semantics evolve] → Match resolved public method symbols and validate the rule against the repository's supported EF Core package; keep claims limited to the detected query shape.
- [Inline-only analysis misses common composed-query locals] → Document the first-version boundary and consider bounded reaching definitions after real-project validation.
- [Complex selectors produce unstable or expensive analysis] → Use a bounded recursive operation classifier, cancel promptly, and reject unsupported nodes instead of descending indefinitely.
- [EFD006 may also inspect the same include chain] → Keep both rules independent because they describe different risks; deterministic ordering and one EFD017 result per projection prevent duplication within the new rule.
- [A developer intentionally leaves a redundant include for readability] → Provide standard suppression with a recorded justification; retain high confidence because the include remains behaviorally ineffective for the supported projection shape.

## Migration Plan

Ship EFD017 enabled by default with the other analyzers, add its release metadata and rule documentation, register it in the CLI, and validate focused plus full-suite behavior. Rollback consists of removing the EFD017 analyzer registration and its implementation/tests/docs; no persisted data, public report field, network service, dependency migration, or JSON schema change is involved.
