# Design

## Context

`EfQueryOperationAnalysis.TryAnalyzeSource` proves that a query comes from a `DbSet<T>` or `DbContext.Set<T>()`. It walks back through any `Queryable` method plus a fixed set of EF composition methods, and it follows query locals whose value is statically determined (`EfQueryLocals`). `UnorderedPaginationAnalyzer` (EFD014) is the closest existing rule. It resolves `Queryable` by symbol, uses `NormalizeMethod` so reduced and static syntax are handled the same way, and walks the chain for ordering operators. `RedundantIncludeAnalyzer` (EFD025) established the tight diagnostic span, which runs from the member name to the end of the invocation.

For EF Core's own behavior: in relational providers, `SelectExpression.ApplyOrdering` clears the existing orderings, unless a limit, an offset, or `Distinct` forces a subquery pushdown first. So an `OrderBy` placed after an existing ordering, with nothing in between that pushes the query down, replaces that ordering in the SQL. See `proposal.md` for the motivation, and the EFD029 spec delta for the observable behavior.

## Goals / Non-Goals

**Goals:**

- Report only when the earlier ordering is provably discarded: both orderings are in one inline chain, and only operators that cannot give the first ordering a purpose sit between them.
- Reuse the shared origin proof and method normalization rather than building a new chain walker.

**Non-Goals:**

- Following the earlier ordering through locals, fields, or helper methods (see the decision below).
- LINQ-to-Objects ordering. There, `OrderBy` is a stable sort, so `OrderBy(a).OrderBy(b)` sorts by `b` and then by `a`. That is a different and sometimes deliberate idiom.
- A code fix.

## Decisions

### Trigger on the replacing ordering, then walk back through a pass-through allow-list

Register one compilation-start action that resolves `Queryable`, `DbSet<T>`, `DbContext`, and `EntityFrameworkQueryableExtensions`, and optionally `RelationalQueryableExtensions`. Then register an invocation action. The action continues only when the normalized target method is `OrderBy` or `OrderByDescending` on `Queryable`.

From the invocation's source, walk back inline. After unwrapping conversions and parentheses:

- A pass-through operator continues the walk. The pass-through operators are `Queryable.Where`, the EF methods in `ElementPreservingEfMethods`, and the relational methods in `ElementPreservingRelationalMethods`. Each EF or relational method must resolve to its EF extension class, the same check `TryAnalyzeSource` uses.
- A `Queryable` `ThenBy` or `ThenByDescending` is recorded as discarded, and the walk continues.
- A `Queryable` `OrderBy` or `OrderByDescending` is recorded as discarded and ends the walk as a *hit*.
- Anything else ends the walk as a *miss*: `Skip`, `Take`, `Distinct`, `Select`, any other `Queryable` method, a local, a parameter, a field, or an unresolved or unrelated call. This includes a `ThenBy` chain that doesn't reach an `OrderBy`.

On a hit, call `TryAnalyzeSource` on the source of the earlier `OrderBy` to prove the EF origin. That part of the chain may pass through locals, as in every other rule. Only the stretch between the two orderings must be inline.

An allow-list was chosen over a block-list (every `Queryable` method except `Skip`, `Take`, and `Distinct`). With an allow-list, an unknown or future operator can only cost recall, never produce a false positive. Reusing the shared method sets keeps EFD029 aligned with EFD017 and EFD025 when EF adds composition methods.

### Don't follow the earlier ordering through locals

The common intentional shape is a default ordering followed by an optional override:

```csharp
IQueryable<T> q = db.Items.OrderBy(i => i.Name);
if (sortByDate) q = q.OrderBy(i => i.Created);
```

`EfQueryLocals` can prove that `q` holds `db.Items.OrderBy(...)` at the read inside the `if`. Following it would report deliberate overriding code at `Warning`. The ordering that gets discarded is a default, not a lost intent. Inline `OrderBy(a).OrderBy(b)` has no such reading, which is why the trigger stays high-confidence. Revisit this if validation shows straight-line cases worth catching.

### Anchor on the replacing call, and report each replacing call once

The diagnostic belongs to the replacing call. With reduced syntax, it spans from the `OrderBy` name in the member access to the end of the invocation, as EFD025 does. With static syntax, it spans the whole invocation. Each `OrderBy` gets at most one finding, because it looks back only to the nearest earlier `OrderBy`. So `OrderBy(a).OrderBy(b).OrderBy(c)` yields two findings, and replacing each reported call with `ThenBy` fixes the chain without contradiction.

### Severity, confidence, and wording

`Correctness`, `DiagnosticSeverity.Warning`, confidence `high`. This matches EFD014 and EFD022, the other rules for silent wrong results. Evidence names the resolved replacing method, the discarded operators in source order (for example `OrderBy -> ThenBy`), and the origin syntax. Impact explains that only the last ordering is translated and that ties come back in an order the database doesn't guarantee, without claiming a cost. Remediation is `ThenBy`/`ThenByDescending` for a combined sort, or deleting the earlier ordering when a replacement was intended.

### Keep CLI integration centralized

Register the analyzer once in `WorkspaceAnalyzer` and add an `AnalyzerReleases.Unshipped.md` entry. The mapper, ordering, rendering, exit codes, suppression, and test-project policy are unchanged.

## Risks / Trade-offs

- **Some providers might not discard the first ordering, for example a hypothetical provider that keeps it.** → The rule targets EF Core's documented query semantics, where `OrderBy` starts a new primary ordering. Even where the database kept both, the code doesn't express `ThenBy` intent. The high confidence rests on the inline shape.
- **The inline-only boundary misses straight-line local reassignments.** → This is deliberate, per the decision above. It is documented on the rule page.
- **An in-memory provider (`UseInMemoryDatabase`) uses LINQ-to-Objects semantics.** → The code is still wrong for the relational production provider, which is the reason the query is written this way. The finding stands.

## Migration Plan

Ship EFD029 enabled by default and register it in the CLI. Add release metadata, the rule page, and the README, package README, and CHANGELOG entries, then run the focused and full suites. To roll back, remove the registration, analyzer, tests, fixture, and docs. No persisted data, report field, dependency, or JSON schema changes.
