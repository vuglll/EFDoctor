# Tasks

## 1. Shared EF async operation recognition

- [x] 1.1 Extract EFD011's core EF async operation classification (queryable-extension `*Async`, `SaveChangesAsync` with override walking, `DbSet<T>.FindAsync`) into a shared internal helper and switch EFD011 to it; verify the unchanged `SyncOverAsyncEfOperationAnalyzerTests` suite passes.
- [x] 1.2 Add the extended EFD018 set (`AddAsync`/`AddRangeAsync` on `DbContext` and `DbSet<T>`, relational `RelationalDatabaseFacadeExtensions.ExecuteSql*Async` when resolvable), and confirm `ExecuteUpdateAsync`/`ExecuteDeleteAsync` resolve on `EntityFrameworkQueryableExtensions` in EF Core 10; verify focused tests recognize each operation and ignore same-named non-EF methods.

## 2. Discard detection and reporting

- [x] 2.1 Add `UnawaitedAsyncEfOperationAnalyzer` with EFD018 descriptor metadata (Correctness, warning), compilation-scoped symbol resolution, concurrent execution, and generated-code exclusion; verify the descriptor test and the generated-code negative pass.
- [x] 2.2 Implement the consumer walk through parentheses, implicit conversions, `ConfigureAwait(...)`, and null-conditional access, classifying expression statements, discard assignments, and void-returning lambda bodies as discards and every other consumer as observed; verify tests cover statement, discard, `ConfigureAwait`, null-conditional, lambda, awaited, returned, stored, passed, composed, task-returning-lambda, and blocked shapes.
- [x] 2.3 Emit one high-confidence finding per discarded expression, spanning the complete expression, with operation and discard form in evidence, qualified impact, await-first remediation with the scoped-context alternative, and documentation key `EFD018`; verify exact spans, properties, and multiple-finding behavior, and that EFD011 and EFD018 never report the same expression.

## 3. Precision and suppression coverage

- [x] 3.1 Add at least ten positive and ten negative EFD018 analyzer fixtures spanning sync and async containing methods, query terminals, save, find, add, bulk and raw-SQL operations, static and reduced syntax, derived contexts, unrelated methods, malformed or `dynamic` code, and generated code; verify the theory suite reports exactly the expected counts without analyzer exceptions.
- [x] 3.2 Add pragma, `.editorconfig`, and `SuppressMessage` cases; verify each standard suppression path excludes EFD018 while an unsuppressed sibling still reports.
- [x] 3.3 Run `dotnet test tests/EFDoctor.Analyzers.Tests/EFDoctor.Analyzers.Tests.csproj --no-restore --filter "FullyQualifiedName~UnawaitedAsyncEfOperationAnalyzerTests|FullyQualifiedName~SyncOverAsyncEfOperationAnalyzerTests"`; verify all focused tests pass with no warnings or errors.

## 4. CLI integration

- [x] 4.1 Register EFD018 in `WorkspaceAnalyzer` and add analyzer release metadata; verify the solution builds and existing CLI tests stay green.
- [x] 4.2 Add a buildable `EFD018.Sample` fixture (allowing CS4014 and EFD018 as warnings) with discarded, awaited, returned, blocked, and suppressed operations, and add it to `EFDoctor.sln`; verify `dotnet build tests/Fixtures/EFD018.Sample/EFD018.Sample.csproj --no-restore` succeeds and the CLI reports stable coordinates.
- [x] 4.3 Add process-level console and JSON tests for EFD018 count, title, high confidence, warning severity, exact ranges, operation and discard-form evidence, remediation, schema version 1, deterministic ordering, suppression, no ANSI output, and exit code `1`; verify the focused EFD018 end-to-end test passes.
- [x] 4.4 Extend the shared `EFD005.TestSample` test-project fixture with an EFD018 case; verify the finding remains present with informational severity and advisory confidence.

## 5. Documentation and verification

- [x] 5.1 Add `docs/rules/EFD018.md` covering triggers, supported operations, discard forms, observed-task exclusions, the CS4014 and EFD011 relationships, evidence, impact, remediation including the scoped-context alternative, confidence, and suppression; verify every example corresponds to a tested fixture shape.
- [x] 5.2 Update README rule status, rule table, sequencing, suppression pointer, rule boundary section, and verification map for EFD018; verify documented coverage matches `WorkspaceAnalyzer` and the release table.
- [x] 5.3 Build `EFDoctor.sln` and run the full suite with the documented .NET 10 `--no-restore` commands; verify zero build warnings or errors and all tests pass.
- [x] 5.4 Run `openspec validate add-efd018-unawaited-async-ef-call --strict`; verify the proposal, spec deltas, design, and completed checklist are coherent and valid.
