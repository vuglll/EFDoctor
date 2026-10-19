# Proposal

## Why

The most common cause of a slow EF Core query is a filter on a column that no index supports, so the database scans the table. The model snapshot that EF Core migrations maintain records every index and key, and the queries record every filter. EFDoctor already reads the snapshot for EFD003, and proves query predicates for EFD009 and EFD023. Joining the two gives a static, local signal: "this query filters only on columns no index leads". The roadmap calls it potentially the highest-impact static finding.

## What Changes

- Add **EFD036, query filter on a column without an index**: `Performance`, `Info`, **advisory** confidence. Whether an index pays off depends on table size, selectivity, and write load, which the analyzer can't see.
- **The predicates:** EFD036 considers the predicate lambda of a `Where` or a predicate terminal (`First`, `Any`, `Count`, …, and their async forms) over a proven EF Core query, including query locals and self-composed locals. It collects the predicate's *filter columns*: the top-level `&&` terms of the form
  - `x.Column == value`;
  - `x.Column < value` (or `<=`, `>`, `>=`);
  - `x.Column.StartsWith(value)`;
  - `values.Contains(x.Column)`.

  `x.Column` must be a mapped, non-`bool` scalar property read directly from the lambda's entity parameter, and `value` must not read the parameter.
- **The finding:** EFD036 reports the first filter column when the compilation contains a model snapshot that maps the entity type, and none of the predicate's filter columns is the leading column of an index, primary key, or alternate key of the entity or its base types.
- **Not reported:**
  - an entity whose snapshot coverage is inconclusive, or that isn't in a snapshot of the same compilation;
  - a predicate with any covered filter column;
  - terms inside `||`;
  - navigation paths;
  - non-sargable shapes, which EFD009 and EFD023 cover.

## Capabilities

### New Capabilities

- `efd036-unindexed-predicate-column`: detection, boundaries, finding contract, suppression, and tests for EFD036.

### Modified Capabilities

None. EFD003's behavior doesn't change. Its snapshot collector becomes shared code.

## Impact

- **Code:**
  - a new `UnindexedPredicateColumnAnalyzer`, which reports at compilation end;
  - `MissingForeignKeyIndexAnalyzer`'s snapshot collector, made internal for reuse;
  - registration in `WorkspaceAnalyzer` and the release file.
- **Tests:**
  - analyzer fixtures;
  - `tests/Fixtures/EFD036.Sample` with a snapshot, and end-to-end tests;
  - an `EFD005.TestSample` case, if the shared test sample can hold a snapshot.
- **Docs:** `docs/rules/EFD036.md`, the rules README, the README, `PACKAGE.md`, usage, suppression, the roadmap, the development history, and the CHANGELOG.
- **Validation:** a corpus re-run, with every finding triaged, and detection precision reported.
