# Design

## Context

See [proposal.md](proposal.md) for motivation and the EFD006 and CLI delta specifications for observable behavior.

EFDoctor already proves inline EF query origins by walking resolved Roslyn invocation operations through `Queryable` and EF Core composition to `DbSet` or `DbContext.Set<TEntity>()`. EFD006 must additionally classify navigation-selector types, retain distinct root-level `Include` paths, observe a later `AsSplitQuery`, and emit only one diagnostic for a fluent query chain. The analyzer targets `netstandard2.0`, runs concurrently, and must remain local and stateless.

Static analysis cannot know configured global query-splitting behavior, actual collection cardinality, generated SQL details, or measured runtime cost. The finding therefore uses medium confidence and qualified language.

## Goals / Non-Goals

**Goals:**

- Prove the chain originates from EF Core and that calls resolve to EF Core query extensions.
- Identify distinct root-level collection navigation properties from semantic selector operations, including filtered includes.
- Recognize `AsSplitQuery` anywhere in the same inline fluent chain and suppress the warning.
- Emit one deterministic finding at the second distinct collection include with actionable evidence.
- Preserve existing reporting, suppression, privacy, and concurrency behavior.

**Non-Goals:**

- Read or execute the EF model, connect to a database, inspect generated SQL, or estimate row counts.
- Infer global `UseQuerySplittingBehavior` configuration or provider-specific defaults.
- Analyze nested sibling branches expressed with repeated `ThenInclude` paths in the first version.
- Follow query variables, helpers, members, arguments, or interprocedural data flow.
- Diagnose duplicate includes, recommend automatic rewrites, or add a code fix.

## Decisions

### 1. Analyze the outermost supported inline query-composition invocation

The analyzer will register for invocation operations, normalize reduced extension methods, and identify invocations that are not themselves the source of a containing supported `Queryable` or EF composition call. It will recursively walk that outer invocation's source chain once, preserving source order and collecting EF operations.

This lets EFD006 observe `AsSplitQuery` even when it appears after the includes and prevents nested invocations in a three-include chain from producing duplicate diagnostics. A chain assigned immediately after its final include remains analyzable because that final include is itself the outermost invocation.

Alternative considered: report while visiting each `Include`. That cannot see a later `AsSplitQuery` and can produce multiple warnings for one chain.

### 2. Restrict sibling detection to distinct root-level Include navigation properties

Only resolved generic `EntityFrameworkQueryableExtensions.Include` calls with lambda selectors participate. `ThenInclude` is retained as ordinary chain evidence but is excluded from root sibling counting. For each `Include`, the analyzer unwraps expression-tree conversions and supported query operators inside the selector to find the property reached directly from the lambda parameter. The selected property type must be an array or implement `IEnumerable<T>`, excluding `string`.

Paths are keyed by property symbol, not display text, so repeated syntax for one navigation does not create a second sibling. Reference navigation includes do not count. Static and reduced extension syntax share the same symbol-based checks.

Alternative considered: count every `Include` call. That would warn on reference-only joins, duplicates, and nested branches, materially increasing false positives. Full branch-tree cardinality analysis is deferred until it can be validated separately.

### 3. Treat explicit AsSplitQuery as a definitive inline exclusion

A resolved EF Core `AsSplitQuery` anywhere in the proven chain suppresses EFD006 because EF Core executes the included collections in separate SQL queries rather than one sibling join product. `AsSingleQuery` remains eligible and is recorded in evidence.

The analyzer does not infer global query-splitting configuration. This creates known false negatives only when source uses a stored query boundary and known reviewable false positives when splitting is configured externally; the finding and documentation will name that limitation.

Alternative considered: suppress whenever splitting mode is unknown. That would eliminate most useful findings because application configuration is commonly outside the analyzed expression.

### 4. Reuse origin tracing but keep EFD006-specific chain details isolated

Existing operation normalization, unwrapping, invocation-source access, type hierarchy checks, and EF origin proof will be reused. EFD006-specific data—ordered include candidates, selector property symbols, collection classification, split-query presence, and the second-collection location—will live in its analyzer rather than broadening the shared result type for unrelated rules.

The walker stops at explicit client boundaries and unsupported calls, consistent with EFD004/EFD005 precision boundaries. The resulting analyzer has no mutable shared state and is safe for concurrent execution.

### 5. Emit qualified evidence at the decision point

The descriptor title is `Multiple sibling collection includes may create a cartesian explosion`, severity is warning, confidence is medium, and documentation key is `EFD006`. The diagnostic spans the complete `Include` invocation that introduces the second distinct collection.

Evidence names the proven `DbSet` origin and ordered distinct navigation paths. Impact explains multiplicative rows and duplicated principal columns as a possibility, not a measurement. Remediation recommends inspecting generated SQL and cardinality and considering `AsSplitQuery`, projection, or separate queries while warning that split queries add round trips and can have consistency trade-offs.

### 6. Integrate additively through existing CLI and test infrastructure

EFD006 adds one analyzer registration and one unshipped release row. Existing finding mapping, sorting, de-duplication, console/JSON schema version `1`, exit codes, and Roslyn suppression remain unchanged.

Focused analyzer tests will exceed ten positive and ten negative cases. A dedicated fixture will cover unsuppressed, split-query, and suppressed cases without changing prior fixture counts; end-to-end tests will verify exact coordinates, structured properties, ordering, and absence of suppressed findings.

## Risks / Trade-offs

- **[Global split-query configuration is invisible]** → Use medium confidence, document the limitation, and recommend suppression with a specific justification when configuration is verified.
- **[Collection-shaped properties are not always mapped navigations]** → Require a proven EF query and resolved `Include`, but acknowledge that EF model validity is ultimately checked by EF; avoid claiming definite runtime cost.
- **[Nested sibling collections are missed]** → State the root-level boundary explicitly and defer branch-tree support until precision fixtures exist.
- **[Stored-query boundaries create false negatives]** → Keep the first version expression-local and explain the limitation rather than adding speculative data flow.
- **[Split queries add round trips and consistency considerations]** → Present `AsSplitQuery` as one review option, not an unconditional fix.
- **[Outermost-chain recognition could regress across operation shapes]** → Cover reduced/static calls, later composition, materializers, parentheses, and multiple independent chains in focused tests.

## Migration Plan

1. Add the EFD006 analyzer and focused semantic fixtures without changing existing analyzers.
2. Register it in the CLI, add a dedicated fixture, and verify console/JSON reporting and suppression.
3. Add release metadata and documentation, then run the full solution regression and strict OpenSpec validation.

Rollback is additive: remove the analyzer registration, source, tests, fixture, documentation, release row, and EFD006 README sections. No persisted data, public JSON schema, or external migration is involved.
