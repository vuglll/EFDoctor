# Tasks

## 1. Share predicate-scan infrastructure

- [x] 1.1 Extract `EfPredicateScan` from `NonSargableCaseTransformAnalyzer`. It holds the predicate operator sets, predicate recovery, the proof of origin and entity type, the nested-lambda-free body walk, and `TryGetColumnPath`. Move EFD009 onto it without changing behavior. Verify: `NonSargableCaseTransformAnalyzerTests` and `Efd009FixtureProducesOnlyUnsuppressedCaseTransformFindings` pass unchanged.
  - Note: EFD009's three findings are unchanged after the move. The EFD009 end-to-end test was renamed `Efd009FixtureProducesCaseTransformFindingsAndHandsContainsToEfd023` and now also expects the EFD023 finding on the fixture's `ToLower().Contains` case, which EFD009 deliberately leaves to EFD023.

## 2. Build the EFD023 analyzer

- [x] 2.1 Add `LeadingWildcardSearchAnalyzer` with:
  - the EFD023 descriptor: `Performance` category, `Info` severity, advisory confidence
  - one provider detection per compilation
  - concurrent execution and generated-code exclusion
  - an entry point through `EfPredicateScan`

  Verify: focused tests show that `Where`, a `Queryable` terminal, and an EF async terminal are each evaluated.
- [x] 2.2 Match `string.Contains(string)` and `string.EndsWith(string)` on a column path, directly or through a parameterless `ToLower()`/`ToUpper()`. Match EF Core `DbFunctionsExtensions.Like` (both overloads) on a column path. The searched value or pattern must not reference the predicate parameter. Verify these cases:
  - each form reports, including a case-transformed column and a navigation path
  - these stay clean: `StartsWith`, `StringComparison` and `char` overloads, collection `Contains`, column-to-column searches, computed properties, nested lambdas, and non-EF `Like`
- [x] 2.3 Implement leading-wildcard detection for `Like` patterns: constant, leftmost operand of a concatenation, or leading interpolated text starting with `%` or `_`. Verify: constant, concatenated, and interpolated leading-wildcard patterns report; prefix patterns and patterns of unknown shape stay clean.
- [x] 2.4 Emit one diagnostic per search call, located on the search invocation. Include evidence (origin, operator, column path, search form, case transformation, reason, provider), qualified impact, provider-aware remediation, and documentation key `EFD023`. Verify: tests assert the exact span, severity, confidence, every property, the wording, and all three remediation variants.

## 3. Complete precision and suppression coverage

- [x] 3.1 Add at least ten positive and ten negative EFD023 analyzer fixtures that cover every scenario in the EFD023 spec delta. Confirm every curated source except deliberately malformed ones compiles. Verify: the theory suite reports exactly the expected counts, with no analyzer exceptions.
- [x] 3.2 Add pragma, `.editorconfig`, and `SuppressMessage("Performance", "EFD023")` cases, plus generated-code and no-EF-reference cases. Verify: each suppression removes only its target.
- [x] 3.3 Run `dotnet test tests/EFDoctor.Analyzers.Tests/EFDoctor.Analyzers.Tests.csproj --no-restore --filter "FullyQualifiedName~LeadingWildcardSearchAnalyzerTests|FullyQualifiedName~NonSargableCaseTransformAnalyzerTests"`. Verify: all tests pass with no warnings or errors.

## 4. Integrate EFD023 into the CLI

- [x] 4.1 Register `LeadingWildcardSearchAnalyzer` in `WorkspaceAnalyzer`, and add EFD023 (`Performance`, `Info`) to `AnalyzerReleases.Unshipped.md`. Verify: the build shows no RS2xxx release-tracking warnings.
- [x] 4.2 Add a buildable `tests/Fixtures/EFD023.Sample` project that references SQL Server. Include `Contains`, `EndsWith`, `ToLower().Contains`, a leading-wildcard `Like`, `StartsWith`, a prefix `Like`, and suppressed cases. Nest it under the existing `Fixtures` solution folder, as `CLAUDE.md` describes. Verify: the fixture builds, and `grep -c '"Fixtures", "Fixtures"' EFDoctor.sln` prints `1`.
- [x] 4.3 Add process-level console and JSON tests for the EFD023 sample. Assert: count, title, `Info` severity, advisory confidence, exact ranges, evidence, SQL Server-led remediation, schema version 1, deterministic ordering, suppression, no ANSI output, and exit code `1`. Verify: the focused test passes, and no other rule (in particular EFD009 or EFD022) reports in the fixture.
- [x] 4.4 Add an EFD023 case to `tests/Fixtures/EFD005.TestSample`, and update `TestProjectFindingsFromMultipleRulesAreDowngradedButRemainReported` to the new count and rule order. Verify: with `--include-test-projects` the finding appears at advisory/`Info`.

## 5. Document and verify the change

- [x] 5.1 Add `docs/rules/EFD023.md`. Cover:
  - triggers and pattern shapes
  - the EFD009 and EFD022 boundaries, and the other excluded shapes
  - evidence and impact
  - provider-aware remediation
  - advisory confidence and the `.editorconfig` opt-out
  - the inline-flow limits
  - suppression examples

  Verify: every example matches a tested fixture shape.
- [x] 5.2 Update every documentation location listed in `CLAUDE.md`. That covers the README (table, status, history, category note if needed, suppression paragraph, boundary section, both verification lists), PACKAGE.md, and CHANGELOG. Replace "the planned EFD023" in `docs/rules/EFD009.md` with a link. Verify: the documented coverage matches `WorkspaceAnalyzer` and the release table.
- [x] 5.3 Build `EFDoctor.sln` and run the full automated suite with `--no-restore`. Verify: zero build warnings or errors, and all tests pass.
- [x] 5.4 Run `openspec validate add-efd023-leading-wildcard-search --strict`. Verify: the proposal, spec deltas, design, and completed task checklist are coherent and valid.
