# Design

## Context

EFDoctor analyzers use compilation-scoped Roslyn operation actions, normalize reduced extension methods, prove EF query origins through `EfQueryOperationAnalysis`, and attach a common diagnostic-property contract consumed unchanged by the CLI. `SaveChangesInLoopAnalyzer` already resolves context save methods through override chains, while the query-shape analyzers already distinguish LINQ-to-Objects materializers from EF asynchronous materializers. The CLI centrally registers analyzers and applies a common test-project downgrade. See `proposal.md` for motivation and the EFD013 and CLI deltas for the behavioral contract.

EFD013 needs more statement-level correlation than existing invocation-local rules: one finding depends on a query materializer, a `foreach`, a restricted loop action, and a following save operation all belonging to one proven context. EF Core bulk operations also bypass the change tracker and `SaveChanges`, so the rule must remain a review signal rather than claim an automatically safe rewrite.

## Goals / Non-Goals

**Goals:**

- Correlate a complete same-body materialize/loop/save chain without whole-program analysis.
- Prove supported EF APIs and receiver identity from symbols and operations rather than names or source text.
- Share the existing query-origin, finding, suppression, CLI, and fixture infrastructure.
- Keep the first slice narrow enough that every report can explain why the loop appears uniform.

**Non-Goals:**

- A code fix or automatic translation into an `ExecuteUpdate` setter chain.
- Full control-flow, points-to, interprocedural, alias, or EF-model mapping analysis.
- Detecting per-row calculations, conditional updates, disconnected entities, `RemoveRange`, or arbitrary batch-processing loops.
- Proving provider translation, table-mapping compatibility, concurrency equivalence, interceptor behavior, cascade behavior, or transaction equivalence.

## Decisions

### Analyze `foreach` operations as the correlation anchor

Register a compilation-start operation action for loop operations after resolving `DbContext`, `DbSet<T>`, LINQ materializers, EF query extensions, and the bulk method symbols. Continue only for `IForEachLoopOperation` within one `IBlockOperation`. Starting from the loop makes its collection, iteration variable, body, and neighboring save statement available together and naturally yields one diagnostic per pattern. An invocation-only analyzer was rejected because independently observing the materializer and save would require fragile shared state and deduplication.

### Recover only direct or uniquely initialized materialized sources

Accept either a materializer directly in the loop collection or a local reference whose one preceding initializer in the same executable body is the materializer. For a local, scan writes and uses before the loop: require exactly one initializer, no reassignment, ref/out escape, capture, return, field storage, or other consumption before the loop. Unwrap parentheses, implicit conversions, and the single required await for asynchronous materializers. Use the existing normalized-method and query-source helpers to recognize synchronous `Enumerable.ToList`/`ToArray` and EF `ToListAsync`/`ToArrayAsync`, then require `EfQueryOperationAnalysis.TryAnalyzeSource` to reach a `DbSet` or `DbContext.Set<T>` origin.

This bounded reaching-definition approach matches EFD012's conservative local-origin precedent. General data-flow analysis was rejected for the initial rule because aliases and path joins would enlarge both implementation and false-positive risk.

### Derive and compare stable receiver identities

Add a small internal receiver-identity helper that unwraps an operation into a symbol-rooted path for parameter, local, field, property, and `DbContext.Set<T>` receivers. From the proven query origin, retain both the owning-context identity and, where available, the originating-set identity. Resolve `SaveChanges`/`SaveChangesAsync` through the `DbContext` override chain and require its instance identity to equal the query owner. Resolve delete calls through `DbContext.Remove` or `DbSet<T>.Remove`; require the sole entity argument to be the loop variable and require the receiver to match the context or originating set respectively.

Comparing syntax text was rejected because aliases, shadowed names, and equivalent spelling across distinct receivers make it unreliable. If a stable identity cannot be derived, remain silent.

### Require a structurally pure loop body

Normalize a single expression body and a block body into top-level operations. Classify the whole body as either update or delete; never mix the two.

For update, require one or more simple assignments whose target is a writable, direct property on the iteration variable. Reuse the existing simple-property checks, reject indexers, nested member paths, navigation-like types, compound assignments, increments, and duplicate property targets. Accept right-hand sides only after unwrapping when they are constants, defaults, parameters other than the iteration variable, or stable locals/fields that do not reference the iteration variable. Reject all invocations and other side effects in the body.

For delete, require exactly one semantically resolved `Remove(iterationEntity)` invocation on the matching context or set. Any conditional, branch, nested loop, exception region, local mutation, logging, callback, or additional statement rejects the pattern.

Supporting entity-dependent expressions such as `entity.Count + 1` was considered because `ExecuteUpdate` can translate some of them. It is deferred: determining translatability and behavioral equivalence would materially broaden the first rule.

### Require the next executable sibling to be the matching save

Within the containing operation block, require the operation immediately after the `foreach` to be either a direct supported synchronous save expression or an awaited supported asynchronous save expression on the proven owning context. Do not skip intervening statements and do not accept stored or conditionally awaited save tasks. This provides a clear structural boundary and avoids attributing an unrelated later save to the loop.

### Gate each classification on available bulk APIs

Inspect the resolved EF query-extension type for the corresponding `ExecuteUpdate`/`ExecuteUpdateAsync` or `ExecuteDelete`/`ExecuteDeleteAsync` methods. Do not report a classification when no matching bulk API exists in the analyzed compilation. This prevents guidance that cannot compile for older EF Core references and avoids using the EFDoctor test/runtime version as a proxy for the target project's API surface.

### Report at the materialization with guarded remediation

Locate the diagnostic on the materialization expression because that is the avoidable load and a stable action point. Evidence records the resolved materializer, query origin, update property names or delete action, and resolved save method. Use warning severity and medium confidence. Keep impact phrased as likely transfer, tracking, and command-generation costs, and make remediation a review instruction that names the relevant bulk API plus the change-tracking, concurrency, interceptor, cascade, callback, and transaction caveats.

### Keep CLI integration centralized

Register the analyzer once in `WorkspaceAnalyzer`. Existing diagnostic mapping, deterministic ordering, renderers, exit codes, suppression behavior, privacy guarantees, and the project-level test downgrade remain unchanged. Add a buildable EFD013 fixture with eligible, excluded, and suppressed cases so process-level console and JSON tests exercise the complete path.

## Risks / Trade-offs

- [The strict body and provenance rules miss real bulk-operation opportunities] → Prefer deterministic review signals over broad advice; document the initial limitations and expand only with evidence from real projects.
- [A syntactically uniform loop can still rely on `SaveChanges` semantics] → Use medium confidence and make the semantic differences prominent in every finding and rule document.
- [Receiver aliases prevent identity comparison] → Return unknown and do not report; alias analysis is intentionally outside this slice.
- [Simple source properties are not guaranteed to be mapped to one table] → Describe the finding as a candidate for review and require users to verify provider translation and mapping compatibility.
- [EF Core API shapes evolve] → Match normalized resolved symbols and method families rather than hard-coded signatures, and cover the supported package version plus missing-symbol behavior in tests.
- [EFD005 may also report the same materialization] → Keep both findings because they identify distinct risks, rely on deterministic ordering, and avoid special cross-analyzer suppression that would hide useful evidence.

## Migration Plan

Ship EFD013 enabled by default with the other analyzers, update release metadata and documentation, register it in the CLI, and validate focused plus full-suite behavior. Rollback consists of removing the analyzer registration and EFD013 implementation/tests/docs; no persisted state, network service, public JSON field, or schema migration is involved.
