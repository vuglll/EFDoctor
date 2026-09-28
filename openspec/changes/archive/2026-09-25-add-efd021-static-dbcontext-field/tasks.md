# Tasks

## 1. Analyzer

- [x] 1.1 Add `StaticDbContextFieldAnalyzer` (`EFD021`) in `src/EFDoctor.Analyzers/`, resolving `Microsoft.EntityFrameworkCore.DbContext` in a compilation-start action and registering a `SymbolKind.Field` symbol action. Verify it compiles.
- [x] 1.2 Report when a field is `static`, not `const`, not `IsImplicitlyDeclared`, and its type `IsOrDerivesFrom` `DbContext`; anchor on the field declaration; populate evidence, impact, remediation, and high confidence. Verify a `static AppDbContext` field reports and an instance field does not.
- [x] 1.3 Add the `EFD021` entry to `AnalyzerReleases.Unshipped.md`. Verify the analyzer build produces no RS2000 release-tracking error.

## 2. CLI integration

- [x] 2.1 Register `StaticDbContextFieldAnalyzer` in `WorkspaceAnalyzer.Analyzers`. Verify EFD021 appears in analysis output for a production fixture.

## 3. Fixtures and tests

- [x] 3.1 Add analyzer positive fixtures: a `static` field of a derived `DbContext` type and a `static` field typed as the base `DbContext`. Verify each reports at the field location.
- [x] 3.2 Add analyzer negative fixtures: an instance `DbContext` field, a `static` non-`DbContext` field, an unrelated same-named `DbContext` type, and `#pragma`/`SuppressMessage`/editorconfig suppression. Verify none report.
- [x] 3.3 Add a buildable `tests/Fixtures/EFD021.Sample` project and a CLI end-to-end test asserting console and JSON output, exact coordinates, severity/confidence, JSON schema version `1`, and exit codes.

## 4. Documentation

- [x] 4.1 Add `docs/rules/EFD021.md` describing scope, the static-field detection, exclusions (instance fields, DI singletons, properties), remediation, and suppression. Verify examples agree with fixtures.
- [x] 4.2 Add EFD021 to the README rule table and a CHANGELOG `Unreleased` entry. Verify the description is consistent.

## 5. Verification and finalization

- [x] 5.1 Build the solution from a clean state with the documented .NET command and verify no errors or warnings.
- [x] 5.2 Run the full automated suite and verify all analyzer and CLI tests pass, including the new EFD021 cases, and that no other rule's findings changed.
- [x] 5.3 Sync the `efd021-static-dbcontext-field` delta to the main specs and run strict OpenSpec validation for the change.
