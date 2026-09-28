# Proposal

## Why

An EF Core query materialized without a query-side row limit can transfer and buffer an unexpectedly large result set, increasing database work, memory use, and latency. EFD005 adds an intentionally qualified review signal for this risk while accounting for the product brief's medium-high false-positive rating and avoiding duplicate findings with EFD004.

## What Changes

- Add EFD005 to detect semantically resolved EF Core `ToList` and `ToListAsync` materialization whose inline query chain is proven to originate from `DbSet` and contains no recognized query-side `Take` bound.
- Analyze the complete inline query chain through supported `Queryable` and EF Core composition, including filtering, projection, ordering, `Skip`, `Include`, and tracking modifiers; do not infer EF origin from method names or arbitrary `IQueryable<T>` values.
- Treat a resolved query-side `Take` anywhere before materialization as an explicit bound. Do not treat `Where`, `Distinct`, `Skip`, projection, ordering, or aggregation shape alone as proof of a bounded result.
- Exclude explicit client-evaluation boundaries, unrelated or unresolved materializers, in-memory sequences, and materializers already eligible for EFD004 at the same source location.
- Produce one warning, medium-confidence finding per eligible materialization with exact coordinates, evidence of the resolved materializer and `DbSet` origin, the inspected query chain, qualified impact, remediation, and documentation key `EFD005`.
- Recommend reviewing filtering and paging, streaming, chunking, or purpose-built aggregate/terminal operations without prescribing an arbitrary row limit that could change correctness.
- Document legitimate full-result cases such as exports, batch processing, migrations, cache warm-up, and known-small lookup tables, together with standard Roslyn suppression and justification guidance.
- Register EFD005 with the existing local CLI and preserve console/JSON schemas, deterministic ordering, stable exit codes, local-only execution, and standard suppression behavior.
- Add at least ten positive and ten negative analyzer fixtures, a dedicated CLI fixture, end-to-end console/JSON tests, rule documentation, and a correction to the stale README description of EFD005.

## Capabilities

### New Capabilities

- `efd005-unbounded-query-materialization`: Semantic detection, query-bound classification, overlap exclusions, evidence, qualified remediation, suppression, and verification for potentially unbounded EF Core list materialization.

### Modified Capabilities

None.

## Impact

- Adds an EFD005 analyzer under `src/EFDoctor.Analyzers` and registers it in the existing CLI analyzer set.
- Reuses the existing Roslyn, EF Core semantic references, finding properties, renderers, and EFD004 query-origin concepts; no new production dependency or JSON schema version is required.
- Adds analyzer tests, a dedicated fixture project, CLI end-to-end coverage, `docs/rules/EFD005.md`, release tracking, and README corrections.
- Introduces no query execution, database connection, code fix, network call, telemetry, proprietary suppression mechanism, or automatic insertion of result limits.
