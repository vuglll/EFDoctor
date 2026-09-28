# Tasks

## 1. Build the EFD013 operation-chain foundation

- [x] 1.1 Add `BulkUpdateDeleteAnalyzer` with EFD013 descriptor metadata, compilation-scoped EF/LINQ/bulk-symbol resolution, generated-code exclusion, and a `foreach` operation entry point; verify a focused analyzer test observes supported loops only when the corresponding EF bulk API is available.
- [x] 1.2 Implement direct and uniquely initialized local materializer recovery for synchronous `ToList`/`ToArray` and awaited `ToListAsync`/`ToArrayAsync`, reusing `EfQueryOperationAnalysis` for proven `DbSet`/`DbContext.Set<T>` origins; verify focused tests cover direct collection expressions, local initializers, reassignment, escape, non-EF materializers, and ambiguous origins.
- [x] 1.3 Add symbol-rooted receiver identity for query owners and originating sets, plus semantic `SaveChanges`/`SaveChangesAsync` matching against the immediately following sibling statement; verify tests accept matching overridden context saves and reject different contexts, stored tasks, missing awaits, intervening work, and unrelated same-named methods.

## 2. Classify conservative update and delete candidates

- [x] 2.1 Implement pure update-body classification for one or more direct writable scalar-property assignments with stable entity-independent values; verify tests accept constant, parameter, and stable-local assignments while rejecting entity reads, nested/navigation targets, duplicate targets, compound updates, branches, calls, control-flow transfers, and other side effects.
- [x] 2.2 Implement pure delete-body classification for one resolved `DbContext.Remove` or originating `DbSet.Remove` call on the iteration entity; verify tests accept matching context/set forms and reject mixed actions, wrong entities, `RemoveRange`, unrelated methods, and mismatched receivers.
- [x] 2.3 Emit one medium-confidence warning at the materialization expression with update/delete-specific message, resolved-chain evidence, qualified impact, guarded bulk-operation remediation, and documentation key `EFD013`; verify exact-span and diagnostic-property tests cover both classifications and mention change-tracking, concurrency, interceptor/callback, cascade, and transaction caveats.

## 3. Complete analyzer precision and suppression coverage

- [x] 3.1 Add at least ten positive and ten negative EFD013 analyzer fixtures spanning synchronous/asynchronous update and delete, multiple assignments, direct/local materialization, override resolution, missing bulk APIs, mismatched contexts, ambiguity, extra behavior, and generated code; verify the `BulkUpdateDeleteAnalyzerTests` theory suite reports exactly the expected counts.
- [x] 3.2 Add pragma, editor-configuration, and `SuppressMessage` cases at the materialization diagnostic location; verify all standard suppression tests exclude EFD013 without affecting unsuppressed sibling patterns.
- [x] 3.3 Run the focused analyzer test project with `dotnet test tests/EFDoctor.Analyzers.Tests/EFDoctor.Analyzers.Tests.csproj --no-restore --filter FullyQualifiedName~BulkUpdateDeleteAnalyzerTests`; verify all focused tests pass without analyzer exceptions or new warnings.

## 4. Integrate EFD013 into the CLI

- [x] 4.1 Register EFD013 in `WorkspaceAnalyzer` and add analyzer release metadata; verify the CLI analyzer inventory includes EFD013 while shared mapping, ordering, privacy, and exit-code behavior remain unchanged.
- [x] 4.2 Add a buildable `EFD013.Sample` fixture with eligible update/delete patterns plus excluded and suppressed cases; verify `dotnet build tests/Fixtures/EFD013.Sample/EFD013.Sample.csproj --no-restore` succeeds and its expected source coordinates are stable.
- [x] 4.3 Add process-level console and JSON tests for EFD013 count, title, medium confidence, warning severity, exact ranges, update/delete evidence, guarded remediation, schema version 1, deterministic ordering, suppression, no ANSI output, and exit code `1`; verify the focused EFD013 end-to-end tests pass.
- [x] 4.4 Extend the shared test-project fixture or add an equivalent test-project case for EFD013; verify the finding remains present but is mapped to informational severity and advisory confidence by the existing project-level policy.

## 5. Document and verify the change

- [x] 5.1 Add `docs/rules/EFD013.md` covering trigger, non-triggers, evidence, likely impact, bulk-operation semantic caveats, safe review guidance, confidence, limitations, and suppression examples; verify every example corresponds to a tested fixture shape.
- [x] 5.2 Update the README implementation status and rule sequence for EFD013; verify documented rule coverage matches `WorkspaceAnalyzer` and the analyzer release table.
- [x] 5.3 Build `EFDoctor.sln` and run the full automated suite with the documented .NET 10 `--no-restore` commands; verify zero build warnings/errors and all tests pass.
- [x] 5.4 Run `openspec validate add-efd013-bulk-update-delete-analysis --strict`; verify the proposal, EFD013/CLI spec deltas, design, and task checklist are coherent and valid.
