# Tasks

## 1. Generalize shared analyzer plumbing

- [x] 1.1 Introduce shared diagnostic-property key constants for confidence, evidence, likely impact, remediation, and documentation reference; update EFD001 and `DiagnosticFindingMapper` to use them, and verify all existing EFD001 analyzer and reporting tests remain unchanged and pass.
- [x] 1.2 Generalize `AnalyzerTestHarness` so a test can select an analyzer and configured diagnostic ID, and verify the existing EFD001 suppression and positive/negative fixtures still pass.
- [x] 1.3 Replace the CLI's single-analyzer construction and EFD001-only filter with an immutable registered EFDoctor analyzer set/ID set, initially retaining EFD001 behavior, and verify existing CLI unit and end-to-end tests pass.

## 2. Implement EFD002 semantic matching

- [x] 2.1 Add the EFD002 diagnostic descriptor and compilation-start symbol resolution for `Queryable`, `DbSet<T>`, and EF Core query extensions, and verify descriptor tests assert ID, warning severity, high confidence, documentation key, and non-absolute wording.
- [x] 2.2 Implement semantic recognition of `Queryable.Count` and EF Core `CountAsync`, including reduced/static extension and predicate overload forms, and verify tests reject unresolved and unrelated same-name methods plus `Enumerable.Count`.
- [x] 2.3 Implement conservative synchronous query-source traversal through direct `DbSet`, `DbContext.Set<T>()`, and inline query-shaping chains, and verify tests accept composed EF queries while rejecting arbitrary `IQueryable`, LINQ-to-Objects, and expressions containing an unrelated `DbSet` argument.
- [x] 2.4 Implement wrapper unwrapping and binary-comparison normalization for zero/one compile-time constants and reversed operands, and verify focused tests cover `> 0`, `!= 0`, `>= 1`, `== 0`, `<= 0`, `< 1`, their reversed equivalents, parentheses, implicit conversions, and awaited `CountAsync`.
- [x] 2.5 Reject exact-count and non-direct uses, including `== 1`, thresholds above one, arithmetic, returns, stored-then-compared values, helper-method flow, and unsupported pattern syntax, and verify each boundary has a negative fixture.
- [x] 2.6 Emit one diagnostic on each matching invocation with resolved-method/comparison evidence, likely impact, polarity-correct `Any`/`AnyAsync` remediation, and preserved-predicate guidance; verify multiple-call, exact-span, and required-property tests pass.

## 3. Complete analyzer fixtures and suppression coverage

- [x] 3.1 Assemble at least ten distinct positive EFD002 fixtures spanning synchronous, asynchronous, cancellation-token, predicate, direct `DbSet`, `Set<T>()`, composed-query, operand-order, and empty/non-empty forms; verify each fixture reports the expected diagnostic count.
- [x] 3.2 Assemble at least ten distinct negative EFD002 fixtures spanning non-EF methods, LINQ-to-Objects, unknown providers, unresolved calls, exact-count uses, stored values, comments, strings, inactive code, and non-count consumers; verify none emits EFD002.
- [x] 3.3 Add `#pragma`, configured severity/editor configuration, and supported `SuppressMessage` tests with intentional-use justifications, and verify the effective analyzer results exclude EFD002 while unsuppressed neighboring matches still report.
- [x] 3.4 Add regression cases for query-source traversal and Roslyn operation shapes, including reduced and static extension syntax, awaits, conversions, and a predicate containing unrelated EF expressions; verify the suite produces no duplicate diagnostics or analyzer exceptions.

## 4. Integrate EFD002 with CLI reporting

- [x] 4.1 Register `CountUsedForExistenceAnalyzer` beside EFD001 in the CLI analyzer set and map all registered non-suppressed EFDoctor diagnostics generically; verify CLI unit tests receive EFD002 findings with every finding-contract field populated.
- [x] 4.2 Extend reporting tests with mixed EFD001/EFD002 findings at overlapping and distinct locations, and verify console and schema-version-1 JSON output preserve deterministic path/span/rule-ID ordering without a schema-version change.
- [x] 4.3 Add or extend a buildable fixture project with positive, negative, and suppressed EFD002 examples, and verify the fixture builds successfully on .NET 10 without requiring EFDoctor runtime network access.
- [x] 4.4 Extend process-level tests to run console and JSON scans against the EFD002 fixture, and verify actionable content, exact source coordinates, JSON purity, schema version 1, exit code `1`, suppression, and repeatable output ordering.
- [x] 4.5 Re-run clean and invalid-path CLI scenarios and verify exit codes remain `0` for a successful scan without findings and `2` for invalid input or analysis failure.

## 5. Documentation and release metadata

- [x] 5.1 Create `docs/rules/EFD002.md` with triggering/non-triggering examples, positive and empty-set `Any`/`AnyAsync` replacements, performance caveats, the stored-value and unknown-`IQueryable` limitations, and all supported suppression mechanisms; verify every documented example agrees with an analyzer fixture.
- [x] 5.2 Update README rule-documentation links and local execution guidance as needed, and verify the documented command produces both console and JSON EFD002 output against the fixture.
- [x] 5.3 Add EFD002 to analyzer release tracking and review production project dependencies for telemetry, HTTP, or remote-service additions; verify the release metadata names EFD002 and no network or telemetry dependency was introduced.

## 6. Final verification

- [x] 6.1 Build the entire solution from a clean state with the documented .NET 10 command and verify it completes with no errors or warnings.
- [x] 6.2 Run the full automated test suite and verify all EFD001 regressions plus at least ten positive and ten negative EFD002 fixtures and all CLI end-to-end checks pass.
- [x] 6.3 Run strict OpenSpec validation for `add-efd002-count-existence-analysis` and verify the proposal, capability spec, design, and task checklist are coherent and valid.
- [x] 6.4 Manually scan the EFD002 fixture in console, JSON, quiet, and no-color modes and verify actionable evidence, deterministic findings, stable exit codes, no ANSI in automation output, and no runtime network or telemetry activity.
