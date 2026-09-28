# Tasks

## 1. EFD006 Semantic Analyzer

- [x] 1.1 Implement outermost inline EF query-chain recognition with resolved `Queryable`/EF composition, `DbSet` origin proof, client-boundary rejection, and `AsSplitQuery`/`AsSingleQuery` observation; verify focused origin, boundary, static-call, and split-mode tests pass.
- [x] 1.2 Implement semantic root navigation extraction and collection classification for direct and filtered `Include` selectors, deduplicated by property symbol while excluding references and `ThenInclude` branches; verify sibling, duplicate, reference, filtered, and nested-path tests pass.
- [x] 1.3 Emit exactly one EFD006 diagnostic per eligible fluent chain at the second distinct collection `Include`, with warning severity, medium confidence, deterministic path evidence, qualified impact, safe remediation, and documentation key; verify exact-location and diagnostic-property assertions pass.
- [x] 1.4 Add EFD006 to unshipped analyzer release metadata and verify the analyzer project builds without a new production dependency.

## 2. Analyzer Test Coverage

- [x] 2.1 Add at least ten positive cases covering extension/static syntax, direct/composed sources, two/three siblings, filtered includes, explicit single-query mode, later query composition/materialization, and multiple independent chains; verify counts, one-finding-per-chain behavior, and exact locations.
- [x] 2.2 Add at least ten negative cases covering references, duplicate paths, nested `ThenInclude`, split queries before/after includes, arbitrary/stored/in-memory queries, client boundaries, unrelated/unresolved methods, comments/strings/inactive code, and generated code; verify no false positives.
- [x] 2.3 Add pragma, editor-configuration, and supported `SuppressMessage` tests with recorded justifications; verify suppressed findings are absent and unsuppressed findings retain complete actionable properties.

## 3. CLI Integration and Documentation

- [x] 3.1 Register EFD006 in the CLI analyzer set and create a dedicated compilable EFD006 fixture with unsuppressed, split-query, and pragma/editor-config/attribute-suppressed cases; verify the fixture builds without changing prior fixture counts.
- [x] 3.2 Add end-to-end console and JSON tests for the EFD006 fixture; verify exact ranges, medium confidence, evidence, deterministic ordering, schema version `1`, suppression, and successful-scan-with-findings exit code.
- [x] 3.3 Add `docs/rules/EFD006.md` and update README status, semantic boundary, split-query limitation, remediation trade-offs, suppression, and verification maps; verify documented paths and commands are valid.

## 4. Final Verification

- [x] 4.1 Restore dependencies, build the solution, run all automated tests, run strict OpenSpec validation, and inspect production changes to verify EFD001-EFD006 pass with no warnings/errors and EFD006 adds no network, telemetry, database execution, target-code execution, code fix, output-schema change, or proprietary suppression behavior.
