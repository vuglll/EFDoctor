# Design

## Context

See `proposal.md` for motivation. EFDoctor analyzers resolve EF Core markers in a compilation-start action and register per-operation actions. `EfQueryOperationAnalysis` exposes public helpers — `TryAnalyzeSource` (traces an invocation's inline source to a `DbSet` origin) and `NormalizeMethod` — that this rule reuses without modification. EFD022 adds no code to the shared analysis surface, so it stays isolated from parallel query-rule work.

## Goals / Non-Goals

**Goals:**

- Detect a `StringComparison` string comparison inside a proven EF Core query predicate, with near-zero false positives.
- Keep the analyzer self-contained: a new file plus registration only.

**Non-Goals:**

- Flagging a `StringComparison` call nested inside an in-memory `IEnumerable` operator within a predicate (for example over a navigation via `Enumerable.Any`); the immediate enclosing operator must be `Queryable`. Deferred to avoid over-reach.
- Detecting other non-translatable constructs (culture-aware `ToUpper`/`ToLower`, `CompareTo`); those are separate rules.
- Proving the query actually executes; the shape alone is the signal.

## Decisions

### 1. Anchor on the string call, walk up to the enclosing Queryable operator

Register `OperationKind.Invocation`. A candidate is an invocation whose resolved target is a `System.String` method (instance `Equals`/`StartsWith`/`EndsWith`/`Contains`/`IndexOf`, or static `string.Equals`/`string.Compare`) that has a parameter of type `System.StringComparison`. From the candidate, walk ancestors to the nearest `IAnonymousFunctionOperation` (the lambda), then to that lambda's consuming `IInvocationOperation` (the operator). Report when the operator's resolved containing type is `System.Linq.Queryable` and `TryAnalyzeSource(operator)` reaches a `DbSet` origin.

Anchoring on the string call gives a precise diagnostic location and reports each offending call exactly once, with no cross-operator de-dup needed (predicate lambdas in a chain are siblings, not nested).

Alternative considered: register on `Queryable` operators and scan their lambda descendants. Rejected: the up-walk anchor is more precise and avoids double-counting when lambdas of chained operators appear in one syntax tree.

### 2. Require the immediate operator to be Queryable and DbSet-sourced

Requiring the consuming operator to be `System.Linq.Queryable` excludes in-memory `Enumerable` LINQ, and `TryAnalyzeSource` reaching a `DbSet` excludes arbitrary `IQueryable` parameters and non-EF sources. Both keep false positives near zero. A `StringComparison` call nested inside an `Enumerable` operator within an EF predicate is consequently not reported (documented limitation).

### 3. Confidence and framing

High confidence: EF Core provably cannot translate these overloads and throws at runtime. Category is correctness; severity `Warning`, mapped from high confidence. The central test-project downgrade still applies.

## Risks / Trade-offs

- **[A StringComparison call inside a navigation `Any`/`All` within a predicate is missed]** → Accepted and documented; the common direct `Where(x => x.Name.Equals(v, sc))` shape is covered, and under-reporting is safer than over-reporting.
- **[A custom `IQueryable` provider that does translate StringComparison]** → Rare; such providers are not EF Core, and the finding is suppressible. EFD022 targets EF Core `DbSet`-sourced queries specifically.

## Migration Plan

1. Add `StringComparisonInPredicateAnalyzer` (EFD022) with the up-walk detection.
2. Register it in `WorkspaceAnalyzer` and add the `AnalyzerReleases.Unshipped.md` entry.
3. Add analyzer fixtures/tests and a buildable CLI fixture with end-to-end coverage.
4. Add `docs/rules/EFD022.md`, the README rule-table row, and a CHANGELOG entry.
5. Sync the new `efd022-stringcomparison-in-predicate` spec and archive.

No persisted data or schema change. Rollback removes the analyzer and its registration.
