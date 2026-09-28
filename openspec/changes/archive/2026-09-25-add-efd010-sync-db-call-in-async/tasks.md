# Tasks

## 1. Analyzer

- [x] 1.1 Add `SyncDatabaseCallInAsyncAnalyzer` (`EFD010`) in `src/EFDoctor.Analyzers/`, resolving `Queryable`, `DbSet`, `DbContext`, `EfExtensions` markers in a compilation-start action and registering an `OperationKind.Invocation` action. Verify it compiles.
- [x] 1.2 Recognize the sync candidates: a LINQ terminal in the recognized set whose `GetInvocationSource` traces via `TryAnalyzeSource` to a `DbSet`; `SaveChanges` on a `DbContext`; `Find` on a `DbSet`. Verify an in-memory terminal and an arbitrary `IQueryable` source are not candidates.
- [x] 1.3 Report only when the nearest enclosing `IAnonymousFunctionOperation` (or, if none, the containing `IMethodSymbol`) is `async`; anchor on the call and name the suggested `*Async` counterpart in evidence/remediation; medium confidence. Verify a sync terminal in an async method reports and the same in a non-async method does not.
- [x] 1.4 Add the `EFD010` entry to `AnalyzerReleases.Unshipped.md`. Verify the analyzer build produces no RS2000 release-tracking error.

## 2. CLI integration

- [x] 2.1 Register `SyncDatabaseCallInAsyncAnalyzer` in `WorkspaceAnalyzer.Analyzers`. Verify EFD010 appears in analysis output for a production fixture.

## 3. Fixtures and tests

- [x] 3.1 Add analyzer positive fixtures: a sync terminal (`ToList`), `SaveChanges`, and `Find` in an async method, and a sync terminal in an async lambda. Verify each reports at the call and names the `*Async` counterpart.
- [x] 3.2 Add analyzer negative fixtures: the same calls in a non-async method, an already-`*Async` call, an in-memory sequence, an arbitrary `IQueryable` parameter, and `#pragma`/`SuppressMessage`/editorconfig suppression. Verify none report.
- [x] 3.3 Add a buildable `tests/Fixtures/EFD010.Sample` project and a CLI end-to-end test asserting console and JSON output, exact coordinates, severity/confidence, JSON schema version `1`, and exit codes.

## 4. Documentation

- [x] 4.1 Add `docs/rules/EFD010.md` describing scope, the async-context requirement, the recognized calls and their `*Async` counterparts, the relationship to EFD011, exclusions, remediation, and suppression. Verify examples agree with fixtures.
- [x] 4.2 Add EFD010 to the README rule table and a CHANGELOG `Unreleased` entry. Verify the description is consistent.

## 5. Verification and finalization

- [x] 5.1 Build the solution from a clean state with the documented .NET command and verify no errors or warnings.
- [x] 5.2 Run the full automated suite and verify all analyzer and CLI tests pass, including the new EFD010 cases, and that no other rule's findings changed.
- [x] 5.3 Sync the `efd010-sync-db-call-in-async` delta to the main specs and run strict OpenSpec validation for the change.
