# Tasks

## 1. Bootstrap the solution

- [x] 1.1 Create `EFDoctor.sln`, `Directory.Build.props`, and `Directory.Packages.props` with nullable analysis, deterministic builds, warnings, and pinned dependencies; verify `dotnet sln EFDoctor.sln list` succeeds.
- [x] 1.2 Create `EFDoctor.Analyzers` targeting `netstandard2.0` and `EFDoctor.Core` plus `EFDoctor.Cli` targeting `net10.0`, add the intended project references, and verify all three production projects compile.
- [x] 1.3 Create `EFDoctor.Analyzers.Tests` and `EFDoctor.Cli.Tests` targeting `net10.0`, add them to the solution, and verify `dotnet test --no-restore` discovers both test assemblies.

## 2. Establish finding and reporting contracts

- [x] 2.1 Implement immutable severity, confidence, source-range, finding, scan-summary, and version-1 report-envelope models in `EFDoctor.Core`; verify unit tests construct a complete EFD001 finding and reject or expose no missing required fields.
- [x] 2.2 Implement centralized deterministic finding ordering by normalized path, start position, end position, and rule ID; verify tests pass the same findings in different discovery orders and receive identical ordering.
- [x] 2.3 Implement human-readable console rendering with complete finding details, summary output, and a plain-text/no-color path; verify snapshot or focused assertions cover findings and the no-findings message without ANSI sequences.
- [x] 2.4 Implement JSON rendering with schema version `1`, summary data, stable field names, complete findings, and no ANSI or extra stdout text; verify serialized finding and empty-result reports parse and match the documented contract.

## 3. Implement EFD001 semantic analysis

- [x] 3.1 Define the EFD001 descriptor and stable diagnostic properties for title, severity, high confidence, evidence, impact, remediation, and documentation key; verify a descriptor metadata test asserts every contract value and the non-absolute wording.
- [x] 3.2 Register compilation-aware invocation-operation analysis and resolve EF Core `DbContext` by metadata name; verify a test compilation without EF Core produces no EFD001 diagnostics and no analyzer failure.
- [x] 3.3 Implement method-symbol matching for EF Core `SaveChanges` and `SaveChangesAsync` overloads, including inherited calls and override chains; verify focused tests distinguish valid base/custom/override calls from unrelated same-name and unresolved calls.
- [x] 3.4 Implement supported loop-ancestor detection for `for`, `foreach`, `await foreach`, `while`, and `do`/`while`, preserving ordinary nested blocks and stopping at lambda, anonymous-function, local-function, and member boundaries; verify focused scope-boundary tests pass.
- [x] 3.5 Emit exactly one diagnostic on each matching invocation expression with the complete stable properties; verify multiple-call and exact start/end line-column tests report the expected diagnostic count and locations.

## 4. Complete analyzer fixture coverage and suppression

- [x] 4.1 Add at least ten positive EFD001 analyzer fixtures covering every supported loop type, sync and async overloads, cancellation-token usage, a custom `DbContext`, an override, nested `if`/`try`/ordinary blocks, and multiple calls; verify the positive suite reports every marked invocation.
- [x] 4.2 Add at least ten negative fixtures covering unrelated same-name methods, calls outside loops, declarations near loop syntax, comments, strings, inactive preprocessor code, unresolved methods, and lambda/anonymous/local-function boundaries; verify the negative suite reports zero diagnostics.
- [x] 4.3 Add pragma and `.editorconfig` suppression tests plus a `SuppressMessage` test for a Roslyn-supported target, all with documented justification examples; verify effective analyzer results exclude each intentionally suppressed EFD001 diagnostic.
- [x] 4.4 Add regression coverage for a loop inside a deferred callable and for immediately invoked or helper-based deferred execution that remains outside first-version interprocedural scope; verify results match the documented lexical-boundary limitation.

## 5. Build CLI analysis orchestration

- [x] 5.1 Implement `efdoctor analyze <path>` parsing with `--format console|json`, `--quiet`, and `--no-color`, plus clear missing, nonexistent, and unsupported-path validation; verify command tests assert messages and exit code `2` for invalid inputs.
- [x] 5.2 Register local MSBuild, load supported solution and project inputs through `MSBuildWorkspace`, collect workspace/load failures, and obtain analyzable C# compilations without invoking restore; verify integration-focused tests load a local fixture project and classify a broken target as exit code `2`.
- [x] 5.3 Run the EFD001 analyzer with effective Roslyn suppression, map diagnostics and zero-based spans into complete one-based findings, and apply centralized ordering; verify mapper/orchestration tests cover complete fields, suppression exclusion, path normalization, and stable ordering.
- [x] 5.4 Route successful results through the selected renderer and return `0` for no findings, `1` for findings, and `2` for invalid input or analysis failure; verify command tests cover all modes and exit-code branches, including JSON-only stdout and no-color behavior.

## 6. Prove the end-to-end vertical slice

- [x] 6.1 Create a buildable `tests/Fixtures/EFD001.Sample` project containing deterministic positive, negative, multiple-call, and suppressed examples; verify the fixture builds independently with locally restored dependencies.
- [x] 6.2 Add a process-level test that invokes the built CLI against the fixture in console mode; verify exit code `1`, deterministic finding order, accurate locations, actionable evidence, and absence of suppressed or negative-case findings.
- [x] 6.3 Add a process-level JSON test against the fixture and parse stdout; verify schema version `1`, complete ordered findings, summary counts, no ANSI/progress text, and exit code `1`.
- [x] 6.4 Add process-level clean-project and invalid-path cases; verify the CLI returns `0` with an empty report for the clean target and `2` with a clear mode-appropriate error for invalid input.
- [x] 6.5 Review production dependencies and exercise the prepared fixture with telemetry disabled and no restore step; verify EFDoctor contains no HTTP/telemetry/update/license component and attempts no product-initiated network operation during analysis.

## 7. Document and validate the change

- [x] 7.1 Update the repository documentation with .NET 10 prerequisites and exact restore, clean build, full test, local console, and local JSON commands; verify a clean-checkout walkthrough completes using only the documented steps.
- [x] 7.2 Document exit codes, JSON schema version `1`, deterministic ordering, every standard suppression form with justification guidance, and the lexical/interprocedural control-flow limitation; verify each documented behavior has a corresponding automated test reference.
- [x] 7.3 Document the local-only guarantee and the MSBuild trust boundary, including that targets must already be locally restorable/buildable and that analyzing an untrusted project can execute its project-authored design-time targets; verify the documentation does not claim sandboxing or zero network activity by user-authored build logic.
- [x] 7.4 Run restore once, then run the complete solution build and test suite from a clean checkout state; verify all projects build, all analyzer/reporting/CLI/end-to-end tests pass, and at least ten positive plus ten negative EFD001 fixtures are present.
