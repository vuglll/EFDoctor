# Tasks

## 1. Share include-path infrastructure

- [x] 1.1 Move the supported element-preserving `Queryable`, EF, and relational method sets from `ProjectionDropsIncludeAnalyzer` into `EfQueryOperationAnalysis`, and have EFD017 use them. Verify: `dotnet test tests/EFDoctor.Analyzers.Tests/EFDoctor.Analyzers.Tests.csproj --no-restore --filter FullyQualifiedName~ProjectionDropsIncludeAnalyzerTests` passes unchanged.
- [x] 1.2 Extract `TryGetIncludeSegments` into an internal `EfIncludePaths` helper that also reports whether filtered-include operators were stripped. Switch EFD017 to the helper and have it ignore the flag. Verify: the EFD017 analyzer suite and the EFD006 `MultipleCollectionIncludeAnalyzerTests` suite pass unchanged.

## 2. Build the EFD025 analyzer

- [x] 2.1 Add `RedundantIncludeAnalyzer` with:
  - the EFD025 descriptor: `Maintainability` category, `Info` severity, high confidence
  - compilation-scoped symbol resolution
  - concurrent execution and generated-code exclusion
  - an entry point limited to the outermost supported call of each inline chain

  Verify: a focused test shows a chain wrapped by `Where`, `ToList`, or `Select` is analyzed exactly once.
- [x] 2.2 Implement the source-ordered chain walk. It proves the `DbSet<T>`/`DbContext.Set<T>()` origin and builds `IncludeChain` entries: expression chains with symbol segments, constant string chains with ordinal segments, and unusable chains for filtered, cast, non-path, or non-constant includes. `ThenInclude` extends the most recent chain. Verify: tests cover reduced and static syntax, nested member paths, and rejection at locals, `Enumerable` calls, unsupported or element-type-changing operators, and unrelated same-named methods.
- [x] 2.3 Implement redundancy classification. A chain is covered when it is a strict prefix of another path. Otherwise it is a duplicate when an earlier chain has the same path. Compare only usable chains of the same kind, and report each chain at most once. Verify these cases:
  - duplicates, including three identical chains
  - covered prefixes in either order
  - sibling `ThenInclude` branches
  - same-named properties on different types
  - string-versus-expression pairs
  - the invariant that the earliest maximal path is never reported
- [x] 2.4 Emit the diagnostic. The span runs from the `Include` name to the end of the chain's last `ThenInclude`, or covers the last invocation for static syntax. Evidence names the origin, the redundant path, the covering or earlier path, and the reason. Add qualified impact, delete-the-chain remediation, and documentation key `EFD025`. Verify: tests assert the exact span, severity, confidence, every property, and the wording.

## 3. Complete precision and suppression coverage

- [x] 3.1 Add at least ten positive and ten negative EFD025 analyzer fixtures that cover every scenario in the EFD025 spec delta, including generated code and malformed code. Verify: the theory suite reports exactly the expected counts, with no analyzer exceptions.
- [x] 3.2 Add pragma, `.editorconfig`, and `SuppressMessage("Maintainability", "EFD025")` cases at the diagnostic location. Verify: each suppression removes only its target, and unsuppressed sibling patterns still report.
- [x] 3.3 Run `dotnet test tests/EFDoctor.Analyzers.Tests/EFDoctor.Analyzers.Tests.csproj --no-restore --filter FullyQualifiedName~RedundantIncludeAnalyzerTests`. Verify: all focused tests pass with no warnings or errors.

## 4. Integrate EFD025 into the CLI

- [x] 4.1 Register `RedundantIncludeAnalyzer` in `WorkspaceAnalyzer`, and add EFD025 (`Maintainability`, `Info`) to `AnalyzerReleases.Unshipped.md`. Verify: the build shows no RS2xxx release-tracking warnings, and the CLI analyzer inventory includes EFD025.
- [x] 4.2 Add a buildable `tests/Fixtures/EFD025.Sample` project with duplicate, covered, branching, filtered, string, and suppressed cases. Add `EFD025` to `WarningsNotAsErrors` if needed. Verify: `dotnet build tests/Fixtures/EFD025.Sample/EFD025.Sample.csproj --no-restore` succeeds.
- [x] 4.3 Add process-level console and JSON tests for the EFD025 sample. Assert: count, title, `Info` severity, high confidence, exact ranges, path evidence, remediation, schema version 1, deterministic ordering, suppression, no ANSI output, and exit code `1`. Verify: the focused EFD025 end-to-end tests pass.
- [x] 4.4 Add an EFD025 case to `tests/Fixtures/EFD005.TestSample`, and update `TestProjectFindingsFromMultipleRulesAreDowngradedButRemainReported` to the new count and rule order. Verify: with `--include-test-projects` the finding appears at advisory/`Info`, and `TestProjectIsSkippedByDefault` still passes.

## 5. Document and verify the change

- [x] 5.1 Add `docs/rules/EFD025.md`. Cover triggers, the branching non-trigger, excluded shapes, evidence, impact, remediation, confidence and severity, the inline-flow and auto-include limits, and suppression examples, including turning the rule off through `.editorconfig`. Verify: every example matches a tested fixture shape.
- [x] 5.2 Update the README: rule table, project status, change sequence, suppression list, and the SuppressMessage category note (EFD025 uses `Maintainability`). Add EFD025 to the CHANGELOG under a new unreleased section. Verify: the documented coverage matches `WorkspaceAnalyzer` and the release table.
- [x] 5.3 Build `EFDoctor.sln` and run the full automated suite with the documented .NET 10 `--no-restore` commands. Verify: zero build warnings or errors, and all tests pass.
- [x] 5.4 Run `openspec validate add-efd025-redundant-include --strict`. Verify: the proposal, spec deltas, design, and completed task checklist are coherent and valid.
