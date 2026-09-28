# Design

## Context

See [proposal.md](proposal.md) for motivation and [the EFD003 specification](specs/efd003-missing-foreign-key-index/spec.md) for observable behavior.

EFDoctor currently runs independent Roslyn analyzers over each loaded C# project and maps their diagnostics into one shared reporting contract. EFD003 differs from EFD001 and EFD002 because a result depends on correlating several model-building operations rather than classifying one invocation in isolation.

EF Core normally creates an index for foreign-key properties, but the convention can be removed or the model can otherwise be customized. EF Core's generated `ModelSnapshot` is the checked-in representation of the current migration model and contains the normalized foreign-key, index, key, and inheritance metadata needed for the check. The snapshot is therefore a stronger local source of evidence than entity attributes or individual `OnModelCreating` calls.

Model snapshots are generated source. EFD003 must deliberately analyze and report diagnostics in generated snapshot files even though the existing invocation analyzers continue to ignore generated code.

## Goals / Non-Goals

**Goals:**

- Correlate foreign keys with indexes, primary/alternate keys, and inherited coverage within each eligible snapshot.
- Reuse the existing diagnostic-to-finding and CLI reporting pipeline without introducing a second result model.
- Keep analysis static, deterministic, concurrent-safe, and independent of EF runtime execution.
- Prefer no result over a low-confidence result when snapshot metadata cannot be reconstructed.

**Non-Goals:**

- Reconstruct the runtime model from arbitrary `OnModelCreating` control flow.
- Compare the snapshot with the deployed database or prove that an external/manual index exists.
- Judge index selectivity, included columns, filters, sort direction, fragmentation, or workload value.
- Add a code fix, migration generator, custom suppression store, or support for non-SQL Server providers.

## Decisions

### 1. Treat each semantic `ModelSnapshot.BuildModel` body as one analysis unit

EFD003 will register at compilation start, resolve `ModelSnapshot`, `ModelBuilder`, the SQL Server provider marker type, and the relevant EF Core builder method definitions, then analyze operation blocks whose owning method overrides `ModelSnapshot.BuildModel`. A single operation walker will collect an immutable intermediate description of the snapshot before evaluating findings.

This makes the correlation boundary explicit, prevents state from leaking between contexts or projects, and permits Roslyn to run different snapshot analyses concurrently. Per-block state remains local; shared mutable compilation state is unnecessary.

Alternatives considered:

- An invocation action cannot reliably know whether a later index or key covers an earlier foreign key.
- A compilation-end accumulator would work but introduces unnecessary synchronization and makes suppression/location handling harder.
- Loading EF Core and executing the snapshot would provide a runtime model but would execute target code and load target dependencies, violating local static-analysis and safety constraints.

### 2. Recognize EF model operations by symbols and snapshot structure

The analyzer will accept only methods whose symbols resolve to the relevant EF Core metadata-builder APIs. It will associate operations with an entity through the containing `ModelBuilder.Entity(...)` configuration lambda. Entity identities come from a constant entity-name argument or a generic entity type symbol.

The collector will recognize:

- relationship chains that establish a dependent `HasForeignKey` property sequence;
- `HasIndex`, `HasKey`, and alternate-key representations;
- `HasBaseType` relationships needed to find inherited coverage; and
- string-array and simple property-expression forms whose ordered property names can be recovered without evaluation.

Transparent conversions and compiler-created parameter arrays will be unwrapped. Any non-constant or unsupported expression invalidates only the affected foreign key or candidate coverage record; it does not guess from source text.

Alternative considered: syntax-only matching is simpler but cannot distinguish unrelated same-named APIs, reliably interpret overloads, or meet the high-confidence contract.

### 3. Use EF-compatible leading-prefix coverage semantics

Within an entity, an index or key covers a foreign key when its property list starts with the entire foreign-key property list in the same ordinal order. Coverage lookup also walks a proven `HasBaseType` chain. Additional trailing properties are allowed; reversed, partial, or non-leading matches are not.

This mirrors the useful part of EF Core's `ForeignKeyIndexConvention` coverage behavior and SQL Server composite-index behavior. EFD003 is a performance check, so it does not require a covering index to be unique for a one-to-one relationship: a non-unique leading-key index still supports lookups, even if it does not enforce uniqueness.

Alternative considered: requiring exact equality would report redundant indexes when a wider index already has the foreign key as its leading prefix.

### 4. Gate the rule on an explicit SQL Server provider reference

At compilation start, EFD003 will require both EF Core snapshot types and an unambiguous metadata type from `Microsoft.EntityFrameworkCore.SqlServer`. This keeps the rule inside EFDoctor's SQL Server scope without relying on fragile generated annotation strings. A migrations project that does not reference the provider is treated as inconclusive and produces no finding.

Alternative considered: detecting annotations such as `SqlServer:*` in the snapshot can miss valid models whose generated snapshot happens not to contain a provider-specific annotation. Treating every relational snapshot as SQL Server would exceed the product scope.

### 5. Report at the foreign-key invocation using the shared diagnostic properties

For each uncovered foreign key, the diagnostic location will be the complete `HasForeignKey(...)` invocation syntax. Diagnostic properties will populate confidence, evidence, likely impact, remediation, and documentation reference using the existing `DiagnosticPropertyNames` contract. The CLI needs only to register the analyzer; existing mapping, de-duplication, ordering, formats, schema version, and exit codes remain unchanged.

The evidence string will identify the snapshot type, dependent entity, and ordered property list. The remediation will recommend model configuration plus a reviewed migration and will explicitly mention intentional tradeoffs and external indexes before suppression.

### 6. Opt EFD003 into generated-code diagnostics without changing other rules

EFD003's analyzer initialization will use Roslyn generated-code analysis and reporting flags because model snapshots normally carry generated-code markers. EFD001 and EFD002 retain their current generated-code exclusion. Standard pragma, editor configuration, and `SuppressMessage` processing remains owned by Roslyn; the CLI continues to request only unsuppressed diagnostics.

### 7. Extend the existing tests with a dedicated snapshot harness and fixture

Analyzer tests will compile representative snapshot source with EF Core relational and SQL Server assemblies available. A small helper will reduce fixture boilerplate while preserving explicit source positions. Tests will cover more than ten positive and ten negative cases, including semantic lookalikes, property extraction, coverage rules, inheritance, multiple diagnostics, locations, and suppression.

A dedicated EFD003 fixture project will contain a compilable snapshot with both covered and uncovered foreign keys. CLI end-to-end tests will run console and JSON scans against it and assert the shared contract. Production analyzer code will not reference EF runtime packages; only tests/fixtures need those compile-time packages.

## Risks / Trade-offs

- **[Snapshot drift]** A checked-in snapshot can lag behind `OnModelCreating`. → State clearly that EFD003 assesses the current checked-in migration model and recommend regenerating migrations before relying on the result.
- **[Generated API shape changes]** Future EF Core versions may emit builder patterns not handled by the property extractor. → Match semantic API families, keep extraction helpers isolated, add version-shaped fixtures, and skip unsupported shapes rather than reporting.
- **[External indexes are invisible]** A DBA-managed index can make the warning unnecessary. → Phrase evidence as absence from the EF model, document verification and standard suppression, and never claim the physical database lacks an index.
- **[Separate migrations project lacks provider reference]** Strict SQL Server gating can create a false negative. → Document the limitation; users can add the normal provider reference to the migrations project. Prefer this miss to a cross-provider false positive.
- **[Inheritance and malformed cycles]** Incorrect or incomplete base metadata could produce bad coverage decisions. → Resolve only constant entity identities, detect cycles, and suppress affected results when the chain is ambiguous.
- **[Diagnostic volume in generated files]** Generated snapshots are not where developers usually edit configuration. → Point remediation to model configuration and migration regeneration while locating the exact snapshot evidence for reproducibility.

## Migration Plan

1. Add EFD003 and tests without changing the shared finding or JSON schema.
2. Register the analyzer in the CLI analyzer set and verify existing EFD001/EFD002 output remains stable.
3. Add the SQL Server test/fixture package references and the dedicated end-to-end fixture.
4. Publish rule documentation describing snapshot scope, limitations, remediation, and standard suppression.

Rollback is additive: remove EFD003 from the CLI analyzer registry and revert its analyzer, tests, fixture, package references, and documentation. No user data or persisted schema migration is involved.
