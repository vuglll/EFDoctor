# Tasks

## 1. Build the EFD029 analyzer

- [ ] 1.1 Add `OrderByReplacesOrderingAnalyzer` with the EFD029 descriptor (`Correctness`, `Warning`, high confidence), compilation-scoped symbol resolution, concurrent execution, and generated-code exclusion. The entry point handles only `Queryable.OrderBy` and `OrderByDescending`, normalized for reduced and static syntax. Verify: the analyzer builds with no warnings, and a same-named non-`Queryable` `OrderBy` produces nothing.
- [ ] 1.2 Implement the inline walk back through the pass-through allow-list. It collects `ThenBy`/`ThenByDescending` as discarded, ends with a hit at an earlier `OrderBy`/`OrderByDescending`, and ends with a miss at anything else, including locals. On a hit, prove the origin with `TryAnalyzeSource` from the earlier ordering's source. Verify: tests cover each pass-through operator, blocking by `Skip`/`Take`/`Distinct`/`Select`, the earlier ordering held in a local, an origin reached through a local, and unproven sources.
- [ ] 1.3 Emit the diagnostic. The span runs from the replacing `OrderBy` name to the end of the invocation, or covers the whole invocation for static syntax. Evidence names the replacing method, the discarded operators in source order, and the origin. Add the qualified impact, the `ThenBy`-or-delete remediation, and documentation key `EFD029`. Verify: tests assert the exact span, severity, confidence, every property, and the wording.

## 2. Complete precision and suppression coverage

- [ ] 2.1 Add at least ten positive and ten negative analyzer cases that cover every EFD029 spec scenario, including three consecutive `OrderBy` calls, generated code, and malformed code. Tag each test with `[Trait("Spec", "efd029-orderby-replaces-ordering/<scenario>")]`. Verify: every spec scenario is tagged, and the focused suite passes with no analyzer exceptions.
- [ ] 2.2 Add pragma, `.editorconfig`, and `SuppressMessage("Correctness", "EFD029")` cases. Verify: each one removes only its target finding, and unsuppressed sibling cases still report.

## 3. Integrate EFD029 into the CLI

- [ ] 3.1 Register the analyzer in `WorkspaceAnalyzer`, and add EFD029 (`Correctness`, `Warning`) to `AnalyzerReleases.Unshipped.md`. Verify: the build shows no RS2xxx release-tracking warnings.
- [ ] 3.2 Add a buildable `tests/Fixtures/EFD029.Sample` project with positive, negative, and suppressed cases, nested under the existing Fixtures solution folder. Verify: `dotnet build tests/Fixtures/EFD029.Sample/EFD029.Sample.csproj --no-restore` succeeds, and the `RepositoryConsistencyTests` solution-folder checks pass.
- [ ] 3.3 Add console and JSON end-to-end tests for the sample. Assert the count, title, `Warning` severity, high confidence, exact ranges, evidence, remediation, schema version `1`, deterministic ordering, suppression, and exit code `1`. Verify: the focused EFD029 end-to-end tests pass.
- [ ] 3.4 Add an EFD029 case to `tests/Fixtures/EFD005.TestSample`, and update `TestProjectFindingsFromMultipleRulesAreDowngradedButRemainReported` to the new count and rule order. Verify: with `--include-test-projects` the finding appears downgraded, and `TestProjectIsSkippedByDefault` still passes.

## 4. Document and verify the change

- [ ] 4.1 Add `docs/rules/EFD029.md`. Cover triggers, pass-through and blocking operators, the local-variable boundary, evidence, impact, remediation, confidence and severity, and suppression. Add an `## EFD029 … boundary` section to `docs/rules/README.md`. Verify: every example matches a tested fixture shape.
- [ ] 4.2 Update the README rule table and project status, `src/EFDoctor.Cli/Package/PACKAGE.md`, the `docs/development.md` implementation history and verification map, `docs/suppression.md`, the `docs/roadmap.md` shipped table and candidate list, and `CHANGELOG.md` under **Unreleased**. Verify: `RepositoryConsistencyTests` passes.
- [ ] 4.3 Build `EFDoctor.sln` and run the full suite with the documented `--no-restore` commands. Verify: zero warnings or errors, and all tests pass.
- [ ] 4.4 Run `openspec validate add-efd029-orderby-then-orderby --strict`, and grep the diff for the private denylist terms. Verify: validation passes, and no denylisted term appears.
