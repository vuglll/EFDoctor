# Tasks

## 1. Test and Fixture Foundations

- [x] 1.1 Add centrally managed EF Core SQL Server package references only to the analyzer test and EFD003 fixture projects, and verify package restore plus a solution build succeeds without adding an EF runtime dependency to `EFDoctor.Analyzers`.
- [x] 1.2 Extend the analyzer test harness with a dedicated model-snapshot source helper and SQL Server metadata references, and verify a minimal generated-style `ModelSnapshot` compiles with no C# errors.

## 2. Semantic Snapshot Analysis

- [x] 2.1 Add immutable snapshot entity, foreign-key, index/key, and inheritance records plus a semantic operation collector for eligible `ModelSnapshot.BuildModel` bodies, and verify focused tests recover constant string-array and simple property-expression metadata while rejecting unrelated or unresolved APIs.
- [x] 2.2 Implement deterministic coverage evaluation for exact matches, ordered leading prefixes, primary/alternate keys, entity isolation, and base-entity inheritance with cycle protection, and verify unit tests cover each coverage branch.
- [x] 2.3 Implement `MissingForeignKeyIndexAnalyzer` with EFD003 descriptor metadata, SQL Server provider gating, generated-code reporting, one diagnostic per uncovered foreign key, exact invocation locations, and complete shared diagnostic properties; verify targeted analyzer tests pass.
- [x] 2.4 Add EFD003 to the unshipped analyzer release notes and verify analyzer packaging/build validation accepts the new supported diagnostic.

## 3. Analyzer Behavior and Suppression Tests

- [x] 3.1 Add at least ten positive EFD003 cases covering single and composite foreign keys, wrong-order and non-leading indexes, wrong-entity indexes, inherited uncovered keys, and multiple findings; verify every expected diagnostic and its location.
- [x] 3.2 Add at least ten negative EFD003 cases covering exact and wider-prefix indexes, primary/alternate keys, inherited coverage, non-snapshot code, non-SQL Server projects, unrelated same-named methods, comments/strings/inactive code, dynamic metadata, and unresolved symbols; verify the suite produces no false positives.
- [x] 3.3 Add pragma, editor-configuration, and supported `SuppressMessage` tests plus evidence/remediation assertions, and verify suppressed diagnostics are absent while unsuppressed findings contain the documented high-confidence contract.

## 4. CLI Integration

- [x] 4.1 Register EFD003 in the CLI analyzer set and verify existing deterministic de-duplication, finding ordering, exit codes, console rendering, JSON schema version, and EFD001/EFD002 tests remain unchanged and passing.
- [x] 4.2 Create a compilable EFD003 SQL Server fixture project containing covered and uncovered snapshot relationships, including suppression examples, and verify the fixture builds independently.
- [x] 4.3 Add end-to-end CLI tests for the EFD003 fixture in console and JSON modes, and verify rule metadata, snapshot evidence, exact locations, deterministic ordering, schema version, and findings exit code.

## 5. Documentation and Final Verification

- [x] 5.1 Add `docs/rules/EFD003.md` and update the README with detection scope, model-snapshot limitation, performance impact, remediation, external-index caveat, and justified standard suppression examples; verify all documented paths and commands are valid.
- [x] 5.2 Build the entire solution from a clean state and run all automated tests, then verify EFD001-EFD003 and CLI suites pass with no warnings or errors and inspect the implementation to confirm analysis performs no network, telemetry, database, target-code execution, or proprietary suppression activity.
