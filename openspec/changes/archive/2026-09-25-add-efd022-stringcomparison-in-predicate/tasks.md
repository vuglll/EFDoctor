# Tasks

## 1. Analyzer

- [x] 1.1 Add `StringComparisonInPredicateAnalyzer` (`EFD022`) in `src/EFDoctor.Analyzers/`, resolving `System.Linq.Queryable`, `DbSet`, `DbContext`, `EfExtensions`, and `System.StringComparison` markers in a compilation-start action and registering an `OperationKind.Invocation` action. Verify it compiles.
- [x] 1.2 Recognize a candidate: a resolved `System.String` method (`Equals`/`StartsWith`/`EndsWith`/`Contains`/`IndexOf`, or static `string.Equals`/`string.Compare`) with a `System.StringComparison` parameter. Verify a non-`StringComparison` overload is not a candidate.
- [x] 1.3 Walk ancestors to the nearest lambda, then to its consuming invocation; report only when that operator's containing type is `System.Linq.Queryable` and `TryAnalyzeSource(operator)` reaches a `DbSet` origin. Anchor on the string call; populate evidence, impact, remediation, and high confidence. Verify a `Where` predicate reports and an in-memory `Enumerable.Where` does not.
- [x] 1.4 Add the `EFD022` entry to `AnalyzerReleases.Unshipped.md`. Verify the analyzer build produces no RS2000 release-tracking error.

## 2. CLI integration

- [x] 2.1 Register `StringComparisonInPredicateAnalyzer` in `WorkspaceAnalyzer.Analyzers`. Verify EFD022 appears in analysis output for a production fixture.

## 3. Fixtures and tests

- [x] 3.1 Add analyzer positive fixtures: `Equals`, `StartsWith`, `EndsWith`, `Contains` with `StringComparison`, and static `string.Equals` inside a `Where` over a `DbSet`. Verify each reports at the string-call location.
- [x] 3.2 Add analyzer negative fixtures: the same methods without a `StringComparison` argument, an in-memory `Enumerable` query, a `StringComparison` call outside any query lambda, an arbitrary `IQueryable` source, and `#pragma`/`SuppressMessage`/editorconfig suppression. Verify none report.
- [x] 3.3 Add a buildable `tests/Fixtures/EFD022.Sample` project and a CLI end-to-end test asserting console and JSON output, exact coordinates, severity/confidence, JSON schema version `1`, and exit codes.

## 4. Documentation

- [x] 4.1 Add `docs/rules/EFD022.md` describing scope, the enclosing-Queryable requirement, the in-memory/non-predicate exclusions, remediation (translatable comparison or collation), and suppression. Verify examples agree with fixtures.
- [x] 4.2 Add EFD022 to the README rule table and a CHANGELOG `Unreleased` entry. Verify the description is consistent.

## 5. Verification and finalization

- [x] 5.1 Build the solution from a clean state with the documented .NET command and verify no errors or warnings.
- [x] 5.2 Run the full automated suite and verify all analyzer and CLI tests pass, including the new EFD022 cases, and that no other rule's findings changed.
- [x] 5.3 Sync the `efd022-stringcomparison-in-predicate` delta to the main specs and run strict OpenSpec validation for the change.
