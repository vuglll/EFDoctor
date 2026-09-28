# Tasks

## 1. Analyzer

- [x] 1.1 Add `MultipleEnumerationAnalyzer` (`EFD020`) in `src/EFDoctor.Analyzers/`, resolving `System.Linq.IQueryable\`1`, `Queryable`, `DbSet`, `DbContext`, `EfExtensions` markers in a compilation-start action and registering a `RegisterOperationBlockAction`. Verify it compiles.
- [x] 1.2 Collect candidate locals: an `IVariableDeclaratorOperation` whose symbol type implements `IQueryable<T>` and whose unwrapped initializer satisfies `TryAnalyzeSource` (proven `DbSet`). Drop any local that is reassigned after declaration. Verify a `var q = ctx.Set.Where(...)` is a candidate and a `var list = q.ToList()` is not.
- [x] 1.3 Count executions per candidate: a `foreach` over the local, or a terminal LINQ operator invocation (`ToList`/`ToArray`/`Count`/`Any`/`First`/`Single`/`Sum`/... and `Async` variants) whose source is the local. Report locals with count ≥ 2, anchored on the declaration; populate evidence (origin + execution count), impact, remediation, and medium confidence. Verify two terminals report and one terminal does not.
- [x] 1.4 Add the `EFD020` entry to `AnalyzerReleases.Unshipped.md`. Verify the analyzer build produces no RS2000 release-tracking error.

## 2. CLI integration

- [x] 2.1 Register `MultipleEnumerationAnalyzer` in `WorkspaceAnalyzer.Analyzers`. Verify EFD020 appears in analysis output for a production fixture.

## 3. Fixtures and tests

- [x] 3.1 Add analyzer positive fixtures: two terminals on the same local, `foreach` plus a terminal, and an `Any` guard followed by enumeration. Verify each reports once at the declaration.
- [x] 3.2 Add analyzer negative fixtures: a single execution, a materialized `ToList` reused, a reassigned local, an arbitrary `IQueryable` parameter, an in-memory sequence, and `#pragma`/`SuppressMessage`/editorconfig suppression. Verify none report.
- [x] 3.3 Add a buildable `tests/Fixtures/EFD020.Sample` project and a CLI end-to-end test asserting console and JSON output, exact coordinates, severity/confidence, JSON schema version `1`, and exit codes.

## 4. Documentation

- [x] 4.1 Add `docs/rules/EFD020.md` describing scope, what counts as an execution, the exclusions (single execution, materialized result, reassignment, cross-method), remediation, and suppression. Verify examples agree with fixtures.
- [x] 4.2 Add EFD020 to the README rule table and a CHANGELOG `Unreleased` entry. Verify the description is consistent.

## 5. Verification and finalization

- [x] 5.1 Build the solution from a clean state with the documented .NET command and verify no errors or warnings.
- [x] 5.2 Run the full automated suite and verify all analyzer and CLI tests pass, including the new EFD020 cases, and that no other rule's findings changed.
- [x] 5.3 Sync the `efd020-multiple-enumeration` delta to the main specs and run strict OpenSpec validation for the change.
