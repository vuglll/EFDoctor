# Proposal

## Why

Every rule that proves an EF Core query chain stops at a local variable, so `var query = …; query.ToList()` looks like an unknown `IQueryable`. The product brief lists "following simple local variables" as a cross-cutting improvement that may be worth more than the next rule (`docs/product-brief.md`, "Cross-cutting improvements"). Storing a query in a local before composing or executing it is idiomatic EF Core, so this boundary costs recall across most rules:

- The shared proof, `EfQueryOperationAnalysis.TryAnalyzeSource`, underlies EFD004, EFD005, EFD009, EFD010, EFD013, EFD014, EFD019, EFD020, EFD022, and EFD023.
- EFD006, EFD017, and EFD025 walk include chains with their own copies of the same inline proof.

Several specs name the boundary explicitly. For example, "Query stored before materialization", "Include chain stored in a local", and "Include chain split across a local" all say "does not report in the first version".

## What Changes

- A shared helper resolves a read of a local to the expression the local holds at that read, but only when that value is **statically determined**:
  - The local is declared with an initializer, and every write to it is a plain `local = value;` statement in the same block.
  - It is never written through `ref`/`out`, a `ref` alias, deconstruction, or compound assignment.
  - The read is in a later statement of that block, not inside a lambda or local function.

  The value is the latest write in an earlier statement, or the initializer. Anything else ends the proof exactly as today.
- The shared chain proof, and the include walkers of EFD006, EFD017, and EFD025, continue through such reads. The proof records each followed local in its operation list as `local '<name>'`, so evidence shows the hop.
- **Conditionally composed queries are not followed.** One example is `if (x) query = query.Where(…)`: the composition at the read isn't known, and a rule that reports a missing bound (EFD005) or a missing ordering (EFD014) could report a false positive. This keeps the rules conservative.
- EFD006 and EFD025 report at an include invocation. When a chain crosses a followed local, the includes in the local's value also belong to the chain that ends at that value's own expression. These rules therefore report an include that lies in a followed local's value only when the later composition is what makes it a finding. Each finding is reported once.
- **Affected rules:**
  - EFD004, EFD005, EFD006, EFD009, EFD010, EFD013, EFD014, EFD017, EFD019, EFD020, EFD022, EFD023, and EFD025 gain recall on queries stored in locals.
  - EFD020 can now track a local whose initializer is itself composed from another query local.
  - Only EFD004, EFD005, EFD006, EFD009, EFD017, EFD023, and EFD025 state the local boundary in their specs, so only they need spec changes.

## Capabilities

### New Capabilities

- `query-chain-local-tracking`: when a query chain continues through a local, how the local's value is chosen, what ends the proof, and how include findings stay unique across a local.

### Modified Capabilities

- `efd004-premature-query-materialization`: the query source may be followed through locals. Materialized results are still not followed.
- `efd005-unbounded-query-materialization`: a query stored in a followable local is analyzed like the equivalent inline query. The stored-query parity rule now applies to followable locals.
- `efd006-multiple-collection-include`: sibling includes split across a followable local are combined.
- `efd009-non-sargable-case-transform` and `efd023-leading-wildcard-search`: a predicate over a followable query local is evaluated. Other stored locals are still unproven.
- `efd017-projection-drops-include` and `efd025-redundant-include`: include state is carried through followable locals.

## Impact

- **Code:**
  - a new `EfQueryLocals` helper, with a per-body cache of local references;
  - `EfQueryOperationAnalysis.TryAnalyzeSourceCore`;
  - the chain walkers in `MultipleCollectionIncludeAnalyzer`, `ProjectionDropsIncludeAnalyzer`, and `RedundantIncludeAnalyzer`, which track whether an include came through a local.
- **Tests:**
  - helper tests for each followability condition;
  - positive local fixtures for each affected rule;
  - EFD006 and EFD025 once-only tests;
  - existing stored-local negative fixtures flipped to positive;
  - tracing for the new capability and for the changed EFD005, EFD006, and EFD023 scenarios.
- **Docs:** the rule pages that describe the local boundary, the README chain-proof wording, the product brief's cross-cutting list, and the CHANGELOG.
- **Validation:** re-run the corpus and triage every new finding before archiving. Expect new findings, and investigate any new false-positive class before shipping.
