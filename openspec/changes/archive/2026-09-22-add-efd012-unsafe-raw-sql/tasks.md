# Tasks

## 1. Implement semantic EFD012 detection

- [x] 1.1 Add `UnsafeRawSqlAnalyzer` with EFD012 metadata, compilation-scoped relational EF symbol resolution, normalized raw-API matching, and SQL-argument selection; verify focused tests reject unrelated same-named methods and accept reduced/static supported forms.
- [x] 1.2 Classify direct interpolation and non-constant string concatenation while preserving constants and parameterized raw SQL; verify focused positive and negative analyzer tests pass.
- [x] 1.3 Add conservative same-body local-origin analysis with cycle and ambiguity protection; verify uniquely initialized/assigned unsafe locals report and multiple-write or unsupported origins do not.
- [x] 1.4 Emit exact-location, high-confidence actionable properties with qualified injection and plan-cache language; verify the property and source-span test passes.

## 2. Complete analyzer validation coverage

- [x] 2.1 Add at least ten positive and ten negative fixtures covering all specified APIs, construction forms, safe alternatives, static/reduced calls, local flow, unrelated methods, ambiguity, and generated code; verify fixture theory counts pass.
- [x] 2.2 Add pragma, editor-configuration, and `SuppressMessage` coverage; verify all three standard suppression mechanisms exclude EFD012.

## 3. Integrate the CLI end to end

- [x] 3.1 Register EFD012 in the shared CLI analyzer set and add release metadata; verify the analyzer is discoverable without changing common mapping behavior.
- [x] 3.2 Add a buildable EFD012 fixture with unsafe, safe, and suppressed examples; verify it builds on .NET 10 without restore or network access.
- [x] 3.3 Add process-level console and JSON tests for EFD012 fields, exact coordinates, deterministic output, schema version 1, exit code `1`, suppression, and no ANSI output; verify the focused CLI tests pass.
- [x] 3.4 Verify the existing shared test-project downgrade retains EFD012 while converting it to advisory/Info in a test project.

## 4. Document and verify the rule

- [x] 4.1 Add `docs/rules/EFD012.md` with trigger, non-triggers, evidence, qualified impact, remediation, identifier allow-list guidance, confidence, limitations, and suppression examples; verify every code example agrees with an analyzer fixture.
- [x] 4.2 Update README implementation status and rule catalog references; verify documented rule coverage matches the registered analyzers.
- [x] 4.3 Build the solution and run the full automated suite with the documented .NET 10 no-restore commands; verify zero build warnings/errors and all tests pass.
- [x] 4.4 Run strict OpenSpec validation for `add-efd012-unsafe-raw-sql`; verify the proposal, specs, design, and completed tasks are coherent and valid.
