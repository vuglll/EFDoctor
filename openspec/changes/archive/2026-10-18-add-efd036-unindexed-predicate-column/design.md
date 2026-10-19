# Design

## Context

- `MissingForeignKeyIndexAnalyzer.SnapshotCollector` walks a snapshot's `BuildModel` and records, per entity name, its covering property lists (indexes and keys), its base entity, and whether coverage is inconclusive.
- `EfPredicateScan` finds SQL-translated predicate lambdas over proven queries.

## Goals / Non-Goals

**Goals:** a low-noise advisory signal for queries that filter only on unindexed columns.

**Non-Goals:**
- snapshots in another compilation (the common split migrations-project layout), which are a documented boundary;
- index usefulness, selectivity, and covering-include analysis;
- provider-specific index types.

## Decisions

### Compilation-end join

**Decision:**
1. An operation-block action on snapshot `BuildModel` methods fills a concurrent map of entity coverage.
2. An invocation action collects candidate predicates with their filter columns.
3. A compilation-end action reports the candidates whose entity is mapped, conclusive, and has no covered filter column.

**Why:** The snapshot and the queries live in different trees. Joining at compilation end needs no cross-tree semantic models (RS1030).

### Report only when no filter column is covered

**Decision:** Collect all the top-level conjunct filter columns of a predicate. Report only when none of them leads an index or key.

**Why:** Once one column of a conjunction can seek, the database narrows the rows with it. Reporting the other columns would be noise. Leading-column matching mirrors EFD003's leading-prefix rule.

### Advisory, Info

**Decision:** Advisory confidence and `Info` severity.

**Why:** Small tables, low selectivity, and indexes managed outside migrations all make a missing index correct. The finding is evidence to check, not a defect.

## Risks / Trade-offs

- **[Risk] Indexes created outside migrations (raw SQL, DBA scripts).** → Document it. Suppress per entity with `.editorconfig`, or per call site.
- **[Trade-off] Split migration projects get no findings.** → Documented. The common single-project layout is covered.
