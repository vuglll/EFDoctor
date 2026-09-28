# Proposal

## Why

Paging an EF Core query with `Skip`/`Take` but no preceding `OrderBy` produces results in an undefined order: the database is free to return rows differently between calls, so page 2 can repeat or omit rows already shown on page 1. This is a genuine correctness bug, not just a performance smell, and it is highly detectable statically with a low false-positive rate. It is the highest value-to-effort rule remaining in the catalog (EFD014, "Later" tier).

## What Changes

- Add rule **EFD014**: report a proven EF Core query that pages with `Queryable.Skip` (optionally followed by `Take`) without any ordering operator (`OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`) earlier in the same inline query chain.
- Require `Skip` (real pagination): a bare `Take` top-N without ordering is frequently intentional and is not reported, which keeps false positives near zero.
- Fire once per paged query, anchored at the outermost paging operator, so `Skip().Take()` yields a single high-confidence finding.
- Register EFD014 in the CLI analyzer set so it flows through the existing reporting, ordering, suppression, privacy, test-project, and exit-code contracts.

## Capabilities

### New Capabilities

- `efd014-unordered-pagination`: Detect EF Core `Skip`/`Take` paging without a preceding ordering operator over a proven database query source.

### Modified Capabilities

None.

## Impact

- New analyzer `src/EFDoctor.Analyzers/UnorderedPaginationAnalyzer.cs` reusing `EfQueryOperationAnalysis` for source tracing and chain inspection.
- `src/EFDoctor.Cli/WorkspaceAnalyzer.cs`: add the analyzer to the analyzer set.
- New rule doc `docs/rules/EFD014.md`; README rule table/summary updated.
- New analyzer fixtures and tests (`tests/Fixtures/EFD014.Sample`, `UnorderedPaginationAnalyzerTests`) plus CLI end-to-end coverage.
- No JSON schema, CLI command surface, exit-code catalogue, network, or telemetry change; other rules are unaffected.
