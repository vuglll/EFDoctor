# Tasks

## 1. Build the EFD009 analyzer

- [x] 1.1 Add `NonSargableCaseTransformAnalyzer` with:
  - the EFD009 descriptor: `Performance` category, `Warning` severity, medium confidence
  - compilation-scoped symbol resolution and one `EfProviderDetection.Detect` call
  - concurrent execution and generated-code exclusion
  - an entry point limited to the recognized `Queryable` and EF async predicate operators with a single-parameter predicate at ordinal 1

  Verify: focused tests show `Where`, a `Queryable` terminal, and an EF async terminal are each evaluated, and index-parameter `Where` and non-LINQ `Where` are ignored.
- [x] 1.2 Prove the source with `EfQueryOperationAnalysis.TryAnalyzeSource`, and require the predicate parameter type to equal the origin's `DbSet<T>` entity type. Verify: tests cover `DbSet` and `Set<T>()` origins, intervening supported composition, static `Queryable.Where` syntax, and rejection of `IQueryable<T>` parameters, stored locals, `AsQueryable()` sources, `IEnumerable<T>`, and predicates after `Select`.
- [x] 1.3 Walk the predicate body without descending into nested lambdas, find parameterless `string.ToLower()`/`ToUpper()` calls whose instance is a mapped string column path, and build the display path. Verify these cases:
  - direct and reference-navigation paths are accepted
  - computed properties, invariant and culture overloads, non-`string` `ToLower`, and nested-lambda occurrences are rejected
- [x] 1.4 Classify the immediate comparison context. The recognized forms are `==`/`!=` in either operand order, static `string.Equals(a, b)`, instance `Equals` in either position, and `StartsWith` with the transformed call as the instance. The comparison value must not reference the predicate parameter. Verify these cases:
  - each form reports
  - `Contains`, `EndsWith`, `StringComparison` overloads, `string.Compare`, a call argument, a transformed comparison value, and column-to-column comparisons stay clean
- [x] 1.5 Emit one diagnostic per transformation call, located on the `ToLower()`/`ToUpper()` invocation. Include evidence (origin, operator, column path, method, comparison kind, detected provider), qualified impact, the provider-aware remediation (SQL Server, Npgsql, or none), and documentation key `EFD009`. Verify: tests assert the exact span, severity, confidence, every property, the wording, and all three remediation variants.

## 2. Complete precision and suppression coverage

- [x] 2.1 Add at least ten positive and ten negative EFD009 analyzer fixtures that cover every scenario in the EFD009 spec delta, including several occurrences in one predicate, generated code, and malformed code. Verify: the theory suite reports exactly the expected counts, with no analyzer exceptions.
- [x] 2.2 Add pragma, `.editorconfig`, and `SuppressMessage("Performance", "EFD009")` cases at the diagnostic location. Verify: each suppression removes only its target, and unsuppressed sibling patterns still report.
- [x] 2.3 Run `dotnet test tests/EFDoctor.Analyzers.Tests/EFDoctor.Analyzers.Tests.csproj --no-restore --filter FullyQualifiedName~NonSargableCaseTransformAnalyzerTests`. Verify: all focused tests pass with no warnings or errors.

## 3. Integrate EFD009 into the CLI

- [x] 3.1 Register `NonSargableCaseTransformAnalyzer` in `WorkspaceAnalyzer`, and add EFD009 (`Performance`, `Warning`) to `AnalyzerReleases.Unshipped.md`. Verify: the build shows no RS2xxx release-tracking warnings.
- [x] 3.2 Add a buildable `tests/Fixtures/EFD009.Sample` project that references SQL Server, with equality, `StartsWith`, reversed-operand, transformed-value, `Contains`, and suppressed cases. Nest it under the existing `Fixtures` solution folder: if `dotnet sln add` creates a duplicate folder, remove it. Verify: `dotnet build tests/Fixtures/EFD009.Sample/EFD009.Sample.csproj --no-restore` succeeds, and the solution has one `Fixtures` folder.
- [x] 3.3 Add process-level console and JSON tests for the EFD009 sample. Assert: count, title, `Warning` severity, medium confidence, exact ranges, column-path evidence, SQL Server-led remediation, schema version 1, deterministic ordering, suppression, no ANSI output, and exit code `1`. Verify: the focused EFD009 end-to-end tests pass, and no other rule reports in the fixture.
- [x] 3.4 Add an EFD009 case to `tests/Fixtures/EFD005.TestSample`, and update `TestProjectFindingsFromMultipleRulesAreDowngradedButRemainReported` to the new count and rule order. Verify: with `--include-test-projects` the finding appears at advisory/`Info`, and `TestProjectIsSkippedByDefault` still passes.

## 4. Document and verify the change

- [x] 4.1 Add `docs/rules/EFD009.md`. Cover:
  - triggers and comparison forms
  - excluded shapes and their reasons (EFD022 and EFD023 boundaries)
  - evidence and impact
  - provider-aware remediation and the expression-index exception
  - confidence
  - the inline-flow and nested-lambda limits
  - suppression examples

  Verify: every example matches a tested fixture shape.
- [x] 4.2 Update the README:
  - rule table, project status, and implementation history
  - suppression list and a boundary section
  - verification map

  Add EFD009 to the PACKAGE.md rule table and the CHANGELOG "Unreleased" section. Verify: the documented coverage matches `WorkspaceAnalyzer` and the release table.
- [x] 4.3 Build `EFDoctor.sln` and run the full automated suite with the documented .NET 10 `--no-restore` commands. Verify: zero build warnings or errors, and all tests pass.
- [x] 4.4 Run `openspec validate add-efd009-non-sargable-case-transform --strict`. Verify: the proposal, spec deltas, design, and completed task checklist are coherent and valid.
