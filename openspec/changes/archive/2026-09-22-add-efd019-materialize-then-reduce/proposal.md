# Proposal

## Why

Materializing an EF Core query with `ToList` or `ToArray` and immediately reducing the buffered result with `First`, `Count`, `Any`, `Sum`, or a similar operator transfers, buffers, and (for entity queries) tracks every row, only to keep one element, a number, or a boolean that the database could have computed directly. The product brief ranks EFD019 as the next recommended rule after EFD017 and EFD018 because this exact shape is common, costly as tables grow, and has almost no legitimate exceptions when the buffered list is an unnamed temporary.

## What Changes

- Add EFD019 detection for semantically proven EF Core queries materialized by `ToList`/`ToArray`, or by an awaited `ToListAsync`/`ToArrayAsync`, whose result is immediately consumed by a supported LINQ-to-Objects reducer or by the `List<T>.Count` or array `Length` property.
- Support element reducers (`First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, and `Last`/`LastOrDefault` when the query is explicitly ordered), existence and quantifier reducers (`Any`, `All`), counts (`Count`, `LongCount`, `.Count`, `.Length`), and aggregates (`Sum`, `Min`, `Max`, `Average`). A predicate or selector argument is accepted only when it has the same conservatively SQL-capable shape EFD004 already accepts.
- Do not report explicit client boundaries (`AsEnumerable`, `AsAsyncEnumerable`), materialized values stored in locals or reused, arbitrary or in-memory queryables, reducers with unsupported lambdas or overloads, operators already covered by EFD004, or look-alike methods.
- Emit a high-confidence performance warning on the materializer, consistent with EFD004. Evidence names the materializer, the query origin, and the reducer, and remediation recommends applying the reducer to the query itself or using its asynchronous EF Core counterpart (for example `FirstAsync`, `CountAsync`, `AnyAsync`).
- EFD005 stops reporting an unbounded materializer that EFD019 reports, in the same way it already yields to EFD004, so one materializer never produces both findings.
- Register EFD019 in CLI workspace analysis and add analyzer, fixture, suppression, console, JSON, documentation, release-metadata, and regression coverage without changing the report schema.

## Capabilities

### New Capabilities

- `efd019-materialize-then-reduce`: Defines conservative semantic detection, exclusions, evidence, remediation, suppression, and validation coverage for EF Core queries materialized and then immediately reduced on the client.

### Modified Capabilities

- `efd005-unbounded-query-materialization`: EFD005 additionally yields to EFD019 when the same materializer is immediately reduced.
- `cli-analysis`: Requires EFD019 to run in CLI analysis and participate in the existing reporting, ordering, suppression, privacy, test-project downgrade, and exit-code contracts.

## Impact

The change adds one Roslyn analyzer that shares the materializer classification and inline source analysis already used by EFD004 and EFD005, adds a small EFD005 yield check, and adds analyzer and CLI fixtures and tests, EFD019 rule documentation, CLI analyzer registration, release metadata, and README status updates. It remains provider-agnostic and local-only, adds no code fix, and does not change the versioned JSON schema.
