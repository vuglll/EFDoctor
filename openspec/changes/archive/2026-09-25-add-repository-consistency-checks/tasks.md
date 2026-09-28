# Tasks

## 1. Fix existing drift

- [x] 1.1 Change the EFD014 analyzer description to say it reports `Skip` pagination only, matching its spec and rule page. Verify: `git grep -n "Skip or Take" -- src docs` finds nothing, and the EFD014 analyzer tests pass.

## 2. Add the consistency checks

- [x] 2.1 Add `RepositoryConsistencyTests` to `EFDoctor.Cli.Tests`, with repository-root discovery, reflection-based discovery of shipped rule IDs and CLI registrations, and one aggregated failure message per check. Verify: the test class compiles, and a deliberately introduced gap produces a message naming the rule and location, then is reverted.
- [x] 2.2 Implement the registration and documentation check for every shipped rule. It covers CLI registration, the rule spec folder, the rule page, the README table row, the README status line and verification-map analyzer list (with range expansion), the PACKAGE.md row, release tracking, and the CHANGELOG. Verify: the check passes on the current repository.
- [x] 2.3 Implement the orphan check for rule pages, rule spec folders, README table rows, and PACKAGE.md rows. Verify: it passes on the current repository.
- [x] 2.4 Implement the solution check: exactly one "Fixtures" solution folder, and every project under `tests/Fixtures` listed in the solution. Verify: it passes on the current repository.
- [x] 2.5 Implement scenario tracing: parse spec scenarios and `[Trait("Spec", …)]` values from test sources, and fail on unknown capabilities or scenarios, on duplicate scenario names in traced capabilities, and on untested scenarios in traced capabilities. Verify: it passes with no traits present.

## 3. Trace EFD023

- [x] 3.1 Tag the EFD023 analyzer and end-to-end tests with a `Spec` trait for every scenario in `efd023-leading-wildcard-search`. Add a focused test only where no existing test covers a scenario. Verify: the trace check passes, and removing one trait makes it fail and name the scenario.

## 4. Consolidate the CLI spec and process notes

- [x] 4.1 Confirm the `cli-analysis` delta removes all 10 per-rule "Run EFDNNN" requirements and adds the general requirement, and that no other spec refers to the removed requirements. Verify: `openspec validate add-repository-consistency-checks --strict` passes.
- [x] 4.2 Update `CLAUDE.md` so that:
  - the documentation checklist is enforced by `RepositoryConsistencyTests`
  - new rules add no `cli-analysis` requirement
  - new or changed rules tag their scenarios
  - analyzer descriptions must not contradict their spec

  Verify: `CLAUDE.md` matches the implemented checks.

## 5. Verify

- [x] 5.1 Build `EFDoctor.sln` and run the full test suite with `--no-restore`. Verify: zero warnings or errors, and all tests pass, including the new consistency tests.
