# Tasks

## 1. Build the EFD017 query-chain foundation

- [x] 1.1 Add `ProjectionDropsIncludeAnalyzer` with EFD017 descriptor metadata, compilation-scoped EF/LINQ symbol resolution, concurrent execution, and generated-code exclusion; verify focused analyzer tests observe only resolved `Queryable.Select` candidates.
- [x] 1.2 Implement direct inline query-chain traversal through supported `Queryable` and EF composition calls, proving `DbSet<T>`/`DbContext.Set<T>()` origins and stopping at locals, members, helpers, custom operators, `Enumerable` calls, or materialization; verify tests cover accepted and rejected boundaries.
- [x] 1.3 Recover expression-based `Include`/`ThenInclude` paths, including filtered includes and static extension syntax, and aggregate them into one projection-level match; verify tests cover single, multiple, nested, filtered, reduced, and static include forms without changing EFD006 results.

## 2. Classify projection shapes and report findings

- [x] 2.1 Implement selector-expression recovery for expression-bodied and single-return block lambdas plus bounded classification of scalar, value, string, anonymous-object, tuple, object-creation, and member-initializer result shapes; verify focused tests accept every specified definitely non-entity shape.
- [x] 2.2 Implement conservative entity-bearing exclusions for the source parameter, wrappers containing it, and direct source-rooted reference or collection leaves, rejecting conditionals or unsupported nodes when classification is ambiguous; verify negative tests cover identity, entity wrapper, navigation, collection, and mixed-result projections.
- [x] 2.3 Emit one high-confidence warning on the complete `Select` invocation with origin, include paths, and projection shape in evidence plus qualified impact, two-branch remediation, and documentation key `EFD017`; verify exact span, severity, confidence, properties, wording, and one-finding-per-projection behavior.

## 3. Complete precision and suppression coverage

- [x] 3.1 Add at least ten positive and ten negative EFD017 analyzer fixtures spanning scalar/DTO projections, scalar navigation access, multiple and nested includes, intervening composition, static calls, entity-bearing projections, local/materialization boundaries, unrelated methods, malformed or ambiguous code, and generated code; verify the theory suite reports exactly the expected counts without analyzer exceptions.
- [x] 3.2 Add pragma, `.editorconfig`, and `SuppressMessage` cases at the projection diagnostic location; verify all standard suppression paths exclude EFD017 while unsuppressed sibling patterns still report.
- [x] 3.3 Run `dotnet test tests/EFDoctor.Analyzers.Tests/EFDoctor.Analyzers.Tests.csproj --no-restore --filter FullyQualifiedName~ProjectionDropsIncludeAnalyzerTests`; verify all focused tests pass with no warnings or errors.

## 4. Integrate EFD017 into the CLI

- [x] 4.1 Register EFD017 in `WorkspaceAnalyzer` and add analyzer release metadata; verify the CLI analyzer inventory includes EFD017 while shared mapping, ordering, privacy, schema-version, and exit-code behavior remain unchanged.
- [x] 4.2 Add a buildable `EFD017.Sample` fixture with eligible, entity-preserving, ambiguous, and suppressed projections; verify `dotnet build tests/Fixtures/EFD017.Sample/EFD017.Sample.csproj --no-restore` succeeds and expected diagnostic coordinates are stable.
- [x] 4.3 Add process-level console and JSON tests for EFD017 count, title, high confidence, warning severity, exact range, include-path evidence, remediation, schema version 1, deterministic ordering, suppression, no ANSI output, and exit code `1`; verify the focused EFD017 end-to-end tests pass.
- [x] 4.4 Extend the shared test-project fixture with an EFD017 case or add an equivalent test fixture; verify the finding remains present but is mapped to informational severity and advisory confidence by the existing project-level policy.

## 5. Document and verify the change

- [x] 5.1 Add `docs/rules/EFD017.md` covering triggers, preserved and unsupported shapes, evidence, likely impact, remediation alternatives, confidence, inline-flow limitation, and suppression examples; verify every example corresponds to a tested fixture shape.
- [x] 5.2 Update README rule status and sequencing for EFD017; verify documented coverage matches `WorkspaceAnalyzer` and the analyzer release table.
- [x] 5.3 Build `EFDoctor.sln` and run the full automated suite with the documented .NET 10 `--no-restore` commands; verify zero build warnings/errors and all tests pass.
- [x] 5.4 Run `openspec validate add-efd017-projection-drops-include --strict`; verify the proposal, EFD017/CLI spec deltas, design, and completed task checklist remain coherent and valid.
