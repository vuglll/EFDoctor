# Tasks

## 1. Model key sources

- [x] 1.1 Add `EfModelKeys` to scan same-compilation fluent `HasKey`, `HasAlternateKey`, `HasForeignKey`, and unique `HasIndex` calls, in lambda and string forms, keeping single-column entries only. Verify with the fixtures in 3.1.
- [x] 1.2 Add the navigation foreign-key convention to `HasKeyMetadata`, using the receiver type and its base types. Verify with the fixtures in 3.2.
- [x] 1.3 Thread an optional `EfModelKeys` through `TryAnalyzeSource` into key classification, and have only EFD005 supply it, lazily. Verify that the full analyzer suite passes, with the other analyzers' tests unchanged.

## 2. Build hygiene

- [x] 2.1 Build the solution with `TreatWarningsAsErrors` and verify there are no analyzer-rule warnings (for example RS1030 or RS1035).

## 3. Tests

- [x] 3.1 Fluent fixtures: not reported for `HasKey` with a lambda, a string, and `nameof`, in both `OnModelCreating` and `IEntityTypeConfiguration<T>`; for `HasAlternateKey`; for `HasForeignKey` through `HasOne().WithMany()`, `HasMany().WithOne()`, and the string form; for `HasIndex().IsUnique()`; and for configuration declared on a base entity type. Reported at advisory confidence for a composite `HasKey`, a composite `HasForeignKey`, and a `HasIndex` without `IsUnique`. Tag each test with its scenario.
- [x] 3.2 Convention fixtures: `OrganizationId` with an `Organization` navigation, including one inherited from a base type, is not reported. `OrganizationId` with only a collection navigation, or with a `string` property, is reported at advisory. `x.Id == id` with no key source is reported at advisory. Tag each test with its scenario.
- [x] 3.3 Confirm that `RepositoryConsistencyTests` passes once the spec is synced.

## 4. Documentation

- [x] 4.1 Update `docs/rules/EFD005.md` with the key sources and their limits: another project, snapshots, and the primary-key convention.
- [x] 4.2 Update the README EFD005 boundary.
- [x] 4.3 Add a `CHANGELOG.md` entry under **Unreleased**.

## 5. Verification

- [x] 5.1 Run the full test suite.
- [x] 5.2 Re-run the corpus with `--skip-restore` for eshop, jellyfin, bitwarden, and smartstore. Record the EFD005 before-and-after counts and the analysis times in `validation/FINDINGS.md`, then regenerate `SUMMARY.md`.
- [x] 5.3 Run `openspec validate improve-efd005-model-key-sources --strict`.
