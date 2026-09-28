# Tasks

## 1. Shared materialization consumer analysis

- [x] 1.1 Split EFD004's immediate-consumer walk into a shared step that returns the materialized value's outermost wrapper (implicit conversions, parentheses, required `await`), keeping EFD004's operator and argument checks; verify the unchanged `PrematureQueryMaterializationAnalyzerTests` and `UnboundedQueryMaterializationAnalyzerTests` suites pass.

## 2. EFD019 detection and reporting

- [x] 2.1 Add `MaterializeThenReduceAnalyzer` with EFD019 descriptor metadata (Performance, warning), compilation-scoped symbol resolution, concurrent execution, and generated-code exclusion, reusing materializer classification and inline source analysis; verify the descriptor test and generated-code negative pass.
- [x] 2.2 Implement the reducer table (element, ordered element, existence, quantifier, count including `List<T>.Count` and array `Length`, aggregate) with the predicate, selector, and parameterless-aggregate rules; verify focused tests accept every supported form and reject unsupported lambdas, overloads, unordered `Last`, and EFD004 operators.
- [x] 2.3 Emit one high-confidence finding on the complete materializer with materializer, origin, and reducer evidence, qualified impact, and remediation naming the query-side operator or EF Core async counterpart, plus documentation key `EFD019`; verify exact spans, properties, and sync vs. async remediation wording.
- [x] 2.4 Expose the reducer check to EFD005 and skip EFD005 reports at materializers EFD019 reports; verify tests show EFD005 silent for `ToList().First()` and still reporting for an unsupported reduction such as `ToList().First(order => Compute(order))`.

## 3. Precision and suppression coverage

- [x] 3.1 Add at least ten positive and ten negative EFD019 analyzer fixtures spanning sync and awaited materializers, all reducer groups, static and reduced syntax, supported and unsupported lambdas, ordered and unordered `Last`, client boundaries, stored lists, in-memory and arbitrary queryables, unrelated methods, and generated code; verify the theory suite reports exactly the expected counts and that positive cases compile without errors.
- [x] 3.2 Add pragma, `.editorconfig`, and `SuppressMessage` cases; verify each suppression path excludes EFD019 while an unsuppressed sibling still reports.
- [x] 3.3 Run the focused EFD019, EFD004, and EFD005 analyzer tests with `--no-restore`; verify all pass with no warnings or errors.

## 4. CLI integration

- [x] 4.1 Register EFD019 in `WorkspaceAnalyzer` and add analyzer release metadata; verify the solution builds and existing CLI tests stay green, updating EFD005 expectations only where EFD019 now takes over by spec.
- [x] 4.2 Add a buildable `EFD019.Sample` fixture with reduced, query-side, stored, boundary, and suppressed cases, and add it to `EFDoctor.sln`; verify `dotnet build tests/Fixtures/EFD019.Sample/EFD019.Sample.csproj --no-restore` succeeds and the CLI reports stable coordinates without EFD005 duplicates.
- [x] 4.3 Add process-level console and JSON tests for EFD019 count, title, high confidence, warning severity, exact ranges, reducer evidence, remediation, schema version 1, deterministic ordering, suppression, absence of EFD005 at reduced materializers, no ANSI output, and exit code `1`; verify the focused EFD019 end-to-end test passes.
- [x] 4.4 Extend the shared `EFD005.TestSample` test-project fixture with an EFD019 case; verify the finding remains present with informational severity and advisory confidence.

## 5. Documentation and verification

- [x] 5.1 Add `docs/rules/EFD019.md` covering triggers, the reducer table, lambda boundaries, ordered `Last`, exclusions, the EFD004 and EFD005 relationships, evidence, impact, remediation, confidence, and suppression; verify every example corresponds to a tested fixture shape. Update `docs/rules/EFD005.md` to mention the EFD019 yield.
- [x] 5.2 Update README rule status, table, sequencing, suppression pointer, rule boundary section, and verification map for EFD019; verify documented coverage matches `WorkspaceAnalyzer` and the release table.
- [x] 5.3 Build `EFDoctor.sln` and run the full suite with the documented .NET 10 `--no-restore` commands; verify zero build warnings or errors and all tests pass.
- [x] 5.4 Run `openspec validate add-efd019-materialize-then-reduce --strict`; verify the proposal, spec deltas, design, and completed checklist are coherent and valid.
