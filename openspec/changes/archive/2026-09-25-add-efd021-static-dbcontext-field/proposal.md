# Proposal

## Why

An EF Core `DbContext` is not thread-safe and is designed to be short-lived (one per unit of work). Holding one in a `static` field keeps a single instance alive for the whole process and shares it across threads, which causes concurrency exceptions (`A second operation was started on this context...`), stale first-level-cache reads, and an ever-growing change tracker. It is a high-value safety bug with a very low false-positive rate because the shape — a `static` field of a `DbContext`-derived type — is unambiguous and statically decidable.

## What Changes

- Add rule **EFD021**: report a `static` field whose type is, or derives from, `Microsoft.EntityFrameworkCore.DbContext`.
- Report at high confidence, anchored on the field declaration, under a reliability/thread-safety framing.
- Register EFD021 in the CLI analyzer set so it flows through the existing reporting, ordering, suppression, privacy, test-project, and exit-code contracts.

## Capabilities

### New Capabilities

- `efd021-static-dbcontext-field`: Detect an EF Core `DbContext` held in a `static` (process-lifetime) field.

### Modified Capabilities

None.

## Impact

- New analyzer `src/EFDoctor.Analyzers/StaticDbContextFieldAnalyzer.cs` using a symbol action over field symbols and reusing `EfQueryOperationAnalysis.IsOrDerivesFrom`.
- `src/EFDoctor.Cli/WorkspaceAnalyzer.cs`: add the analyzer to the analyzer set; `AnalyzerReleases.Unshipped.md`: register EFD021.
- New rule doc `docs/rules/EFD021.md`; README rule table and CHANGELOG updated.
- New analyzer fixtures and tests (`tests/Fixtures/EFD021.Sample`, `StaticDbContextFieldAnalyzerTests`) plus a CLI end-to-end test.
- No JSON schema, CLI command surface, exit-code catalogue, network, or telemetry change; other rules are unaffected. Detecting a `DbContext` held by a dependency-injection singleton (a "long-lived" instance without a `static` field) is a deliberate out-of-scope limitation for this version.
