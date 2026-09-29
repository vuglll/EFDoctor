# Tasks

## 1. Build the EFD027 analyzer

- [x] 1.1 Add `ConcurrentDbContextOperationAnalyzer` with the EFD027 descriptor (`Reliability`, `Warning`, high confidence), compilation-scoped symbol resolution (`Task`, `Enumerable`, `Queryable`, `DbSet<T>`, `DbContext`, EF extensions), concurrent execution, generated-code exclusion, and one operation-block action. Verify: the analyzer builds with no warnings.
- [x] 1.2 Implement context-key resolution for `SaveChangesAsync`, `FindAsync`, and queryable operations through the proven origin. The key is a never-written local, a never-assigned value parameter, a `this`/static field, an auto-property, or `this`. Verify: tests cover each context form, a reassigned local, a custom-getter property, a method-call context, another object's member, and two factory-created locals.
- [x] 1.3 Implement the combinator-argument trigger over `params`, array-creation, and collection-expression arguments of resolved `Task.WhenAll`/`WhenAny`. Report every same-context operation after the first. Verify: tests cover two and three operations, both combinators, array and collection expressions, mixed contexts, non-EF tasks, and look-alike `WhenAll`.
- [x] 1.4 Implement the unobserved-task-local trigger. It walks later statements of the block in source order, skips lambda and local-function bodies, stops at the first reference to the task local, and reports the first same-context operation. Verify: tests cover two task locals with `WhenAll`, an awaited later operation, awaiting before the next operation, a reference before the next operation, and a later operation only inside a lambda.
- [x] 1.5 Implement the projected-tasks trigger. `Enumerable.Select` with a lambda selector reaches a combinator inline, through `ToList`/`ToArray`, or through a statically determined local. The trigger reports the first operation in the selector whose context is declared outside it. Verify: tests cover an async selector with `FindAsync`, a `ToList` local, a context created inside the selector, and a look-alike `Select`.
- [x] 1.6 Emit the diagnostic on the overlapping operation's invocation, with evidence (method, context, pending operation or task local, shape), impact, the await-or-factory remediation, and documentation key `EFD027`. Guard against duplicate locations. Verify: tests assert the exact span, severity, confidence, every property, and the wording.

## 2. Complete precision and suppression coverage

- [x] 2.1 Add at least ten positive and ten negative analyzer cases that cover every EFD027 spec scenario, including sequential awaits, generated code, and malformed code. Tag each test with literal `[Trait("Spec", "efd027-concurrent-dbcontext-operations/<scenario>")]` attributes. Verify: every scenario is tagged, and the focused suite passes with no analyzer exceptions.
- [x] 2.2 Add pragma, `.editorconfig`, and `SuppressMessage("Reliability", "EFD027")` cases. Verify: each one removes only its target finding, and unsuppressed sibling cases still report.

## 3. Integrate EFD027 into the CLI

- [x] 3.1 Register the analyzer in `WorkspaceAnalyzer`, and add EFD027 (`Reliability`, `Warning`) to `AnalyzerReleases.Unshipped.md`. Verify: the build shows no RS2xxx warnings.
- [x] 3.2 Add a buildable `tests/Fixtures/EFD027.Sample` project with positive, negative, and suppressed cases, nested under the existing Fixtures solution folder. Verify: the fixture builds, and the `RepositoryConsistencyTests` solution checks pass.
- [x] 3.3 Add console and JSON end-to-end tests for the sample. Assert the count, title, `Warning` severity, high confidence, exact ranges, evidence, remediation, schema version `1`, deterministic ordering, suppression, and exit code `1`. Verify: the focused EFD027 end-to-end test passes.
- [x] 3.4 Add an EFD027 case to `tests/Fixtures/EFD005.TestSample`, and update `TestProjectFindingsFromMultipleRulesAreDowngradedButRemainReported` to the new count and rule order. Verify: the test passes.

## 4. Document and verify the change

- [x] 4.1 Add `docs/rules/EFD027.md`. Cover the three shapes, context identity, exclusions, the documented non-goals, evidence, impact, remediation, confidence and severity, and suppression. Add an `## EFD027 … boundary` section to `docs/rules/README.md`. Verify: every example matches a tested fixture shape.
- [x] 4.2 Update the README rule table and project status, `PACKAGE.md`, the `docs/development.md` history and verification map, `docs/suppression.md`, the `docs/roadmap.md` shipped table and candidate list, and `CHANGELOG.md` under **Unreleased**. Verify: `RepositoryConsistencyTests` passes after archive.
- [x] 4.3 Build `EFDoctor.sln` and run the full suite with `--no-restore`. Verify: zero warnings or errors, and all tests pass.
- [x] 4.4 Run `openspec validate add-efd027-concurrent-dbcontext-operations --strict`, and check the diff against the private denylist. Verify: validation passes, and no denylisted term appears.
