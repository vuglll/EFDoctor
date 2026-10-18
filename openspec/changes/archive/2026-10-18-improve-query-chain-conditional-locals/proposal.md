# Proposal

## Why

The chain proof follows a query local only when its value at a read is statically determined, so the most common real-world shape ends the proof:

```csharp
var query = db.Products.AsQueryable();
if (filter.Name != null) query = query.Where(p => p.Name.Contains(filter.Name));
return await query.ToListAsync();
```

When `improve-query-chain-local-tracking` shipped, only 6 new corpus findings appeared, because about 75% of query recompositions are conditional. The roadmap names this the biggest recall lever for the rules that need only to know where a query comes from. Those rules are EFD004, EFD009, EFD010, EFD013, EFD019, EFD022, and EFD023.

## What Changes

- A second, weaker proof, the **origin proof**, is added beside the existing chain proof. It follows a query local whose value at a read is not statically determined, when the local is **self-composed**:
  - it is declared with an initializer, and that initializer is a proven EF Core query;
  - every other write is a plain `local = value;` statement, in any block, branch, loop, lambda, or local function;
  - every such value composes the same local with element-preserving query operators only (`Where`, ordering, `Skip`, `Take`, `Distinct`, and EF's bound-neutral composition such as `Include` or `AsNoTracking`). A conditional expression whose arms are each such a composition, or the local itself, also qualifies.
  
  Any write of a self-composed local keeps the initializer's origin and element type, so every read of the local, wherever it is, has that origin. The proof does not claim to know the composition at the read.
- The origin proof traces the read to the initializer's origin. It records the hop as `local '<name>' (conditionally composed)` in the proven operation list. It records none of the conditional compositions, so the list holds only operations that apply on every path.
- **Rules that opt in:** EFD004, EFD009, EFD010, EFD013, EFD019, EFD022, and EFD023. Each reports only facts about operations at the read or after it, or about predicates in the composition itself, so an unknown conditional composition can't make one of their findings wrong. As a result:
  - EFD009, EFD022, and EFD023 now evaluate a predicate written inside the conditional branch itself, as in `if (x) query = query.Where(p => p.Name.ToLower() == s);`.
  - EFD019 still requires an `OrderBy` on every path before it reports `Last` or `LastOrDefault`.
- **Rules that don't opt in, unchanged:** EFD002, EFD005, EFD006, EFD014, EFD017, EFD020, EFD025, EFD027, EFD037, and EFD038. They report the absence of something (a bound, an ordering, an include, a projection) or count executions, so an unknown conditional composition could make their findings wrong.
- A statically determined local is still followed by the full chain proof, exactly as before. The origin proof applies only where that fails.
- The shared chain proof treats EF Core's own `DbSet<T>.AsQueryable()` instance method as the set it is called on. Until now, `context.Products.AsQueryable()` resolved to that method rather than `Queryable.AsQueryable`, so every rule failed to prove a query that started with it, inline or stored. It is also the usual initializer of a conditionally composed query.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `query-chain-local-tracking`:
  - Adds the origin proof for self-composed query locals, and lists the rules that use it.
  - Narrows the "Conditional recomposition is not followed" scenario to the full chain proof.
  - Treats `DbSet<T>.AsQueryable()` as the set. This adds recall for every rule built on the proof.
- `efd004-premature-query-materialization`: the query source may be traced through a self-composed local.
- `efd009-non-sargable-case-transform` and `efd023-leading-wildcard-search`: a predicate over a self-composed local is evaluated, including a predicate inside the conditional composition. A local that isn't self-composed stays unproven.

EFD010, EFD013, EFD019, and EFD022 gain the same recall through the shared proof. Their specs never stated the local boundary, so they don't change.

## Impact

- **Code:**
  - `EfQueryLocals` records, for each local, its initializer and every plain assignment, and keeps the straight-line followability it has today.
  - `EfQueryOperationAnalysis.TryAnalyzeSource` gains an opt-in parameter for the origin proof.
  - `EfPredicateScan` and the seven rules pass that parameter.
- **Tests:**
  - helper tests for self-composition, each disqualifying write, and the operation list;
  - a positive conditional-local fixture and a negative non-self-composed fixture for each opted-in rule;
  - a negative fixture for EFD005 and EFD014, which stay silent;
  - EFD019's unordered `Last` through a conditional `OrderBy`;
  - tracing for the changed scenarios.
- **Docs:** the roadmap's cross-cutting list, the rule pages that describe the local boundary, the README chain-proof wording if it mentions the boundary, and the CHANGELOG.
- **Validation:** re-run the corpus and triage every new finding before archiving. Any false-positive class blocks shipping.
