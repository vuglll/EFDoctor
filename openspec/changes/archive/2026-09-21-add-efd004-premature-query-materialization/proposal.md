# Proposal

## Why

Materializing an EF Core query before applying translatable filtering, projection, ordering, or paging can transfer and buffer more rows or columns than necessary. EFD004 adds a precision-first diagnostic for the clearest expression-local forms of this problem while avoiding guesses about client-only logic or values used across statements.

## What Changes

- Add EFD004 to detect proven EF Core queries materialized with `ToList`, `ToArray`, `ToListAsync`, or `ToArrayAsync` before a directly composed supported LINQ operator that can safely remain in the SQL query.
- Cover simple `Where`, `Select`, `OrderBy`, `OrderByDescending`, `Skip`, and `Take` compositions whose arguments are conservatively classified as server-translatable.
- Use Roslyn semantic analysis to prove the materializer, EF query origin, downstream LINQ operator, and supported argument shape; do not match method-name text.
- Keep first-version analysis local to one fluent expression. Do not follow materialized values through locals, fields, properties, helper methods, or general data flow.
- Exclude intentional or ambiguous client-side evaluation, unrelated/in-memory LINQ, unresolved calls, and downstream predicates or projections containing unsupported method calls.
- Produce one high-confidence finding per premature materialization with exact source coordinates, actionable evidence, qualified impact, remediation, and EFD004 documentation.
- Preserve the existing local CLI, console/JSON contracts, deterministic ordering, stable exit codes, and standard Roslyn suppression behavior.
- Add comprehensive positive/negative fixtures, suppression coverage, an end-to-end CLI fixture, and rule documentation.

## Capabilities

### New Capabilities

- `efd004-premature-query-materialization`: Semantic detection, exclusions, evidence, remediation, suppression, and verification for EF queries materialized before directly composed SQL-capable operations.

### Modified Capabilities

None.

## Impact

- Adds an EFD004 analyzer under `src/EFDoctor.Analyzers` and registers it with the existing CLI analyzer set.
- Reuses existing Roslyn and EF Core references; no new production dependency or output schema is required.
- Adds analyzer tests, a dedicated CLI fixture, end-to-end console/JSON coverage, `docs/rules/EFD004.md`, and README guidance.
- Introduces no code fix, query execution, database connection, network call, telemetry, or proprietary suppression mechanism.
