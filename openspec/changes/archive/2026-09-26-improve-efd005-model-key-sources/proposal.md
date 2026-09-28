# Proposal

## Why

EFD005 proves a key only from data annotations: `[Key]`, `[ForeignKey]`, and a unique single-column `[Index]`. On the validation corpus, 115 EFD005 findings are equality on a key recognized only by its name, and they report at advisory confidence. The corpus projects define most of those keys another way:

- **Smartstore** configures them with the fluent API in the same project as the queries, for example `HasForeignKey(c => c.ProductVariantAttributeId)`.
- **Bitwarden and Jellyfin** mostly rely on EF Core's foreign-key naming convention: an `OrganizationId` property next to an `Organization` navigation. Jellyfin also keeps its configuration classes in a different project from its queries, where an analyzer cannot see them.

These are "load the children of one known parent" queries. EFD005's own rule page calls that a legitimate full materialization, so it should not report them at all.

## What Changes

- Treat a property as a proven key when fluent configuration in the analyzed project declares it as one of these:
  - a single-column `HasKey`
  - a single-column `HasAlternateKey`
  - a single-column `HasForeignKey`
  - a single-column `HasIndex(...)` followed by `IsUnique()`

  Lambda (`e => e.Prop`) and string (`"Prop"`, `nameof(...)`) forms are recognized, both in `OnModelCreating` and in `IEntityTypeConfiguration<T>.Configure`.
- Treat a property named `<Navigation>Id` as a proven foreign key when the same entity type, or one of its base types, declares a reference navigation named `<Navigation>`. This is EF Core's own foreign-key convention, and it can be read from the entity type even when the configuration lives in another project.
- A proven key from either source is a **strong** bound, exactly like a data annotation, so the query is not reported.
- Composite keys, composite or non-unique indexes, configuration the analyzer cannot see (another project, or a migration snapshot's string-named entity types), and the `Id`/`<Type>Id` primary-key convention stay as they are today: key-by-name, reported at advisory confidence.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd005-unbounded-query-materialization`: "Recognize predicate-derived row bounds" names the model metadata sources that prove a key. It adds fluent configuration and the navigation foreign-key convention.

## Impact

- **Analyzer:**
  - New `EfModelKeys` helper. It scans the compilation's fluent configuration once, lazily, and only when EFD005 needs it.
  - `EfQueryOperationAnalysis` passes the helper through to key classification. Only EFD005 supplies it.
  - The shared EFD004 predicate model and the other analyzers are unchanged.
- **Tests:** new EFD005 fixtures, traced to the new scenarios.
- **Docs:** `docs/rules/EFD005.md`, the README EFD005 boundary, and the CHANGELOG.
- **Validation:** re-run the corpus and record the effect in `validation/FINDINGS.md`.
