# Proposal

## Why

Two sessions now add rules to this repository in parallel. Each rule must appear in about ten places: its spec, rule page, README (several sections), package readme, release tracking, CHANGELOG, CLI registration, and the solution. Nothing checks that. As a result, this has already drifted without any test, review, or `openspec validate` noticing:

- Four rules (EFD014, EFD020, EFD021, EFD022) shipped with missing README and PACKAGE.md entries.
- `dotnet sln add` created a duplicate "Fixtures" solution folder in four of the last five rule commits.
- EFD014's analyzer description still says "Skip or Take", but its spec and rule page say it reports only `Skip`.
- `cli-analysis` has a "Run EFDNNN during workspace analysis" requirement for 10 rules but not for EFD001–005, 010, 014, 020, 021, or 022. The specs don't agree on what they are meant to say.

The project is also preparing for an open-source release. There, outside contributors will change rules, and the specs must stay a trustworthy description of behavior. Syncing has to be checked automatically.

## What Changes

- **Replace per-rule CLI requirements with one general requirement.** Remove the 10 "Run EFDNNN during workspace analysis" requirements from `cli-analysis` and add one requirement that the CLI runs every analyzer the analyzer assembly ships. Each rule's behavior stays in its own capability spec. The shared reporting, test-project, and exit-code requirements already cover every rule.
- **Add repository consistency checks as ordinary tests** that run with `dotnet test`, so every session and every future CI run enforces them:
  - Every diagnostic ID the analyzer assembly exports is registered in the CLI, and has a rule spec, a rule page, a README table row, README status-line and verification-map coverage, a PACKAGE.md row, a release-tracking entry, and a CHANGELOG entry.
  - Nothing is orphaned: no rule page, rule spec, or table row exists for a rule that doesn't exist.
  - The solution has exactly one "Fixtures" solution folder, and every fixture project under `tests/Fixtures` is in the solution.
- **Add scenario-to-test tracing.** A test declares the spec scenario it covers with `[Trait("Spec", "<capability>/<scenario>")]`. Every tag must name an existing scenario. Once any test tags a capability, every scenario in that capability must be tagged. EFD023 is the first traced capability, and other rules opt in as they are next changed.
- **Fix the EFD014 drift:** its analyzer description says `Skip` only, matching its spec.
- **Update `CLAUDE.md`:** the documentation checklist is now enforced by tests, rules no longer add a `cli-analysis` requirement, and scenario tags are expected for new or changed rules.

## Capabilities

### New Capabilities

- `repository-consistency`: Defines the automated checks that keep analyzers, CLI registration, specs, rule documentation, release metadata, the solution, and scenario-level test coverage in sync.

### Modified Capabilities

- `cli-analysis`: Replaces the 10 per-rule "Run EFDNNN during workspace analysis" requirements with one requirement that every shipped analyzer runs during workspace analysis.

## Impact

- **Code:** one new test class in `EFDoctor.Cli.Tests`, scenario tags on the EFD023 analyzer and end-to-end tests, and a one-line change to the EFD014 descriptor description. No analyzer behavior changes, and no report or JSON schema changes.
- **Specs:** `cli-analysis` loses 10 requirements and gains one. The new `repository-consistency` spec is added.
- **Process:** `CLAUDE.md` is updated. The new tests fail for any future rule that skips a documentation location, so both parallel sessions have to keep the repository consistent.
