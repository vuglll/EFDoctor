# Tasks

## 1. Analyzer

- [x] 1.1 Add `UnorderedPaginationAnalyzer` (`EFD014`) in `src/EFDoctor.Analyzers/`, registering an `OperationKind.Invocation` action and resolving `Queryable`, `DbSet`, `DbContext`, `EfExtensions` markers like the other query analyzers. Verify it compiles.
- [x] 1.2 Recognize a resolved `System.Linq.Queryable.Skip`/`Take` invocation, trace it with `EfQueryOperationAnalysis.TryAnalyzeSource`, and report only when the origin is a proven `DbSet` and the traced `Operations` contain no `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending`. Verify with a fixture that `OrderBy(...).Skip(n).Take(m)` does not report and `Skip(n)` alone does.
- [x] 1.3 Emit one finding per paged query by anchoring on the outermost paging operator (skip reporting when the current operator is the immediate `Queryable` source of an enclosing `Skip`/`Take`). Verify `Skip(n).Take(m)` yields exactly one finding on the `Take`.
- [x] 1.4 Require `Skip` in the traced chain (do not report a bare `Take`); report at high confidence; populate evidence (paging operator, origin, observed operators, absence of ordering), impact, and remediation. Verify with fixtures that `Take`-only does not report and `Skip` reports at high.

## 2. CLI integration

- [x] 2.1 Register `UnorderedPaginationAnalyzer` in `WorkspaceAnalyzer.Analyzers`. Verify EFD014 appears in analysis output for a production fixture.

## 3. Fixtures and tests

- [x] 3.1 Add analyzer positive fixtures: `Skip` only, `Skip().Take()`, `Skip` after `Where`, and `Skip().OrderBy()` (ordering after paging still reports). Verify each reports the expected count and location.
- [x] 3.2 Add analyzer negative fixtures: ordering before paging, `Take`-only (not reported), in-memory (`IEnumerable`) paging, arbitrary `IQueryable` parameter, stored-query local, unrelated same-named methods, and `#pragma`/`SuppressMessage`/editorconfig suppression. Verify none report.
- [x] 3.3 Add a buildable `tests/Fixtures/EFD014.Sample` project and a CLI end-to-end test asserting console and JSON output, exact coordinates, severity/confidence, JSON schema version `1`, and exit codes.

## 4. Documentation

- [x] 4.1 Add `docs/rules/EFD014.md` describing scope, the ordering-before-paging rule, confidence, exceptions, and suppression. Verify examples agree with fixtures.
- [x] 4.2 Add EFD014 to the README rule table/summary and any rule inventory. Verify the count and description are consistent.

## 5. Verification and finalization

- [x] 5.1 Build the solution from a clean state with the documented .NET command and verify no errors or warnings.
- [x] 5.2 Run the full automated suite and verify all analyzer and CLI tests pass, including the new EFD014 cases, and that no other rule's findings changed.
- [x] 5.3 Sync the `efd014-unordered-pagination` delta to the main specs and run strict OpenSpec validation for the change.
