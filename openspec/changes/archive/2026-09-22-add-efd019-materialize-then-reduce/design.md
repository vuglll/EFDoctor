# Design

## Context

EFD004 (`PrematureQueryMaterializationAnalyzer`) and EFD005 (`UnboundedQueryMaterializationAnalyzer`) both start from a resolved materializer. `Enumerable.ToList`/`ToArray` count as materializers, as do EF Core `ToListAsync`/`ToArrayAsync` (EFD005 recognizes only the list forms). Both analyzers prove the inline source with `EfQueryOperationAnalysis.TryAnalyzeSource`, which returns the origin, the list of composed operations (for example `Queryable.OrderBy`), and a row bound. EFD004's internal `TryGetEligibleConsumer` walks outward through implicit conversions, parentheses, and one `await` for async materializers, then accepts only `Enumerable.Where`, `Select`, `OrderBy`, `OrderByDescending`, `Skip`, or `Take` with conservatively SQL-capable arguments. EFD005 calls that method to avoid duplicating EFD004 at one materializer. `EfQueryOperationAnalysis.IsPredicateExpression` and `IsEntityPropertyPath` already define the SQL-capable lambda shapes EFD004 accepts.

See `proposal.md` for motivation and the EFD019, EFD005, and CLI spec deltas for observable behavior.

## Goals / Non-Goals

**Goals:**

- Reuse the existing materializer, source, consumer-walk, and lambda-shape logic so EFD004, EFD005, and EFD019 agree on what "immediately consumed" and "SQL-capable" mean.
- Keep the reducer operator set disjoint from EFD004's, so one materializer is reported by at most one of EFD004 or EFD019, and EFD005 yields to both.
- Leave EFD004 and EFD005 behavior unchanged apart from the new EFD005 yield.

**Non-Goals:**

- Following materialized values through locals or reuse analysis.
- Other materializers (`ToHashSet`, `ToDictionary`, `AsEnumerable`), `ElementAt`, `Contains`, `Aggregate`, or chained client operators such as `ToList().Where(...).First()` (EFD004 reports the first client operator there).
- `List<T>` instance methods such as `Find`, `Exists`, or `TrueForAll`.
- A code fix.

## Decisions

### Share the immediate-consumer walk

Split EFD004's `TryGetEligibleConsumer` into a shared step that returns the outermost wrapper of the materialized value (after implicit conversions, parentheses, and the required single `await` for async materializers), followed by EFD004's existing operator and argument checks. EFD019 uses the same shared step, then accepts either an `Enumerable` invocation whose receiver or first argument is that wrapper, or a property reference on it: `Count` on `List<T>` or `Length` on an array. EFD004's tests are the regression guard.

Duplicating the walk in EFD019 was rejected because the three rules must agree exactly on consumer identity for the yield logic to be sound.

### Classify reducers with an explicit table

| Group | Operators | Accepted arguments |
|---|---|---|
| Element | `First`, `FirstOrDefault`, `Single`, `SingleOrDefault` | none, or one predicate |
| Ordered element | `Last`, `LastOrDefault` | as above, and only when the chain contains `Queryable.OrderBy` or `OrderByDescending` |
| Existence | `Any` | none, or one predicate |
| Quantifier | `All` | one predicate |
| Count | `Count`, `LongCount`, `List<T>.Count`, array `Length` | none, or one predicate for the methods |
| Aggregate | `Sum`, `Min`, `Max`, `Average` | none (element type must be a value type or `string`), or one property-path selector |

A predicate must be a single-parameter lambda that satisfies `EfQueryOperationAnalysis.IsPredicateExpression`. A selector must satisfy `IsEntityPropertyPath`. Overloads with comparers, default values, index parameters, or non-lambda delegates are rejected by argument count and shape. This mirrors EFD004's narrow argument policy and keeps the rule explainable. The ordering requirement for `Last` exists because EF Core rejects `Last` on an unordered query, so the suggested rewrite would throw.

### Report on the materializer with reducer-specific remediation

The location is the complete materializer invocation, consistent with EFD004 and EFD005, so all three rules anchor on one span per materializer. Evidence names the resolved materializer, the proven `DbSet` origin, and the reducer (for example `Enumerable.First` or the `List<T>.Count` property). Remediation names the query-side replacement: for synchronous materializers, the same operator on the query (`Count()` for the `.Count`/`.Length` properties); for awaited materializers, the EF Core async counterpart (`FirstAsync`, `CountAsync`, `AnyAsync`, `SumAsync`, and so on). Severity is warning, confidence is high, and the category is Performance.

The impact wording stays qualified: the cost grows with the number of rows the query would return, and entity queries also populate the change tracker.

### Make EFD005 yield to EFD019

Expose EFD019's reducer check as an internal static method that takes the materializer, async flag, `Enumerable` symbol, and query analysis. EFD005 calls it next to its existing EFD004 check and skips reporting when either matches. EFD004 needs no change, because the two operator sets are disjoint.

### Keep CLI integration and policies centralized

Register the analyzer once in `WorkspaceAnalyzer`. Mapping, deterministic ordering, console and JSON rendering, exit codes, privacy behavior, standard suppression, and test-project downgrade stay unchanged. A buildable EFD019 fixture covers positive, excluded, and suppressed shapes at process level, and the shared test-project fixture gains an EFD019 case.

## Risks / Trade-offs

- [Semantic differences between client and SQL reduction, such as string comparison collation in `Min`/`Max` or `First` without ordering] → The remediation tells developers to verify semantics. Results already depend on query order in both forms. Unordered `Last` is excluded because it cannot translate.
- [The buffered list is intentionally reused through another reference] → Not possible for the inline temporary this rule requires. Stored lists are excluded.
- [EFD005 fixtures or tests currently expect a finding on a reduced materializer] → Run the EFD005 suites and end-to-end fixtures, and update expectations only where the spec delta says EFD019 now takes over.
- [Shared consumer-walk refactor regresses EFD004] → Keep EFD004's operator checks intact and run its existing suite unchanged.

## Migration Plan

Ship EFD019 enabled by default, add its release metadata and documentation, register it in the CLI, add the EFD005 yield, and validate focused and full-suite behavior. Rollback consists of removing the registration, the analyzer, its tests and docs, and the EFD005 yield call. No persisted data, public report field, network service, dependency migration, or JSON schema change is involved.
