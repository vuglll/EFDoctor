# Proposal

## Why

Foreign-key lookups and referential operations can become unexpectedly expensive when the current EF Core model does not contain a usable index for the dependent columns. EFD003 completes the validation-MVP rule set with a high-confidence, local-only check based on the generated model snapshot rather than guesses from entity source text.

## What Changes

- Add EFD003 to detect foreign keys in an EF Core SQL Server model snapshot that are not covered by an index or key in the same entity model.
- Use Roslyn semantic analysis of generated `ModelSnapshot.BuildModel` code; do not instantiate a `DbContext`, execute application code, connect to a database, or infer findings from method names alone.
- Treat an index or key as covering a foreign key when the foreign-key property sequence is the leftmost prefix in the same order.
- Produce one high-confidence finding per uncovered foreign key, with the snapshot entity and property sequence as evidence and remediation that acknowledges intentional indexing tradeoffs or externally managed indexes.
- Preserve the existing finding contract, deterministic console/JSON reporting, stable exit codes, and standard Roslyn suppression behavior.
- Add comprehensive analyzer fixtures, suppression coverage, CLI end-to-end coverage, and concise EFD003 documentation.

## Capabilities

### New Capabilities

- `efd003-missing-foreign-key-index`: Semantic detection, evidence, exclusions, suppression, and verification requirements for foreign keys lacking a covering index in the current EF Core SQL Server model snapshot.

### Modified Capabilities

None.

## Impact

- Adds an EFD003 Roslyn analyzer and supporting model-snapshot inspection logic under `src/EFDoctor.Analyzers`.
- Registers EFD003 in the existing CLI analyzer set without changing the command or output schemas.
- Adds EF Core relational/SQL Server snapshot references to analyzer test and fixture projects where required; the analyzer itself remains decoupled from EF runtime packages.
- Adds analyzer and CLI/integration fixtures plus `docs/rules/EFD003.md` and README rule documentation.
- Introduces no network calls, telemetry, database access, code execution, code fix, or publication workflow.
