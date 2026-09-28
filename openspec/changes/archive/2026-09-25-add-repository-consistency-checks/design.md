# Design

## Context

Each rule ships an analyzer in `EFDoctor.Analyzers` and is registered in the private static `Analyzers` array of `WorkspaceAnalyzer` in `EFDoctor.Cli`. It is documented in several fixed places, which `CLAUDE.md` lists as a manual checklist. `EFDoctor.Cli.Tests` references both projects. Its end-to-end tests already locate the repository root by walking up from the test output directory to `EFDoctor.sln`.

Specs live in `openspec/specs/<capability>/spec.md`. Scenarios are `#### Scenario: <name>` headings under `### Requirement:` headings. Test attributes are xUnit `[Fact]`/`[Theory]`, and xUnit supports `[Trait(name, value)]` on methods, including more than one per method. See `proposal.md` for motivation and the two spec deltas for observable behavior.

## Goals / Non-Goals

**Goals:**

- Catch every kind of drift seen so far with checks that run in the normal test suite.
- Give precise failure messages, so a session can fix the problem without investigating.
- Let scenario tracing be adopted one capability at a time, starting with EFD023.

**Non-Goals:**

- Checking that prose in the specs matches analyzer behavior. That is the pre-release audit's job.
- Checking descriptor text against spec text automatically. The EFD014 fix is done by hand, and the policy is recorded in `CLAUDE.md`.
- Tagging all existing rules now. They opt in when next changed.
- Checking the end-to-end list in the README verification map, which describes fixtures in free text.

## Decisions

### One test class in `EFDoctor.Cli.Tests`, reading files directly

Add `RepositoryConsistencyTests` to `EFDoctor.Cli.Tests`, because that project already references both the analyzer and CLI assemblies. It finds the repository root the same way the end-to-end tests do.

Shipped rule IDs come from reflection over the analyzer assembly: every non-abstract `DiagnosticAnalyzer` type with `[DiagnosticAnalyzer]`, instantiated for its `SupportedDiagnostics`. CLI registration comes from reflection over `WorkspaceAnalyzer`'s private static `Analyzers` field. Using reflection avoids widening the CLI's public API just for a test.

Everything else is read as text: specs, rule pages, README, PACKAGE.md, the release-tracking files, CHANGELOG, the solution file, and test sources.

Parsing test sources rather than loading the analyzer test assembly keeps the CLI test project free of a reference to another test project. `[Trait("Spec", "…")]` is a simple, stable pattern to match with a regular expression.

Each check collects every problem before failing, and reports them all in one assertion message.

### Parse README ranges instead of requiring every ID to be listed

The status line and verification list use phrases like "EFD011 through EFD014". The check extracts explicit IDs and expands every `EFDnnn through EFDmmm` range. Checking for literal IDs was rejected, because it would force the README into long ID lists and punish the readable form it already uses.

### The rule table and package table are keyed by row

The README table row is matched as `| **EFDnnn** |` and the package row as `| EFDnnn |`, the formats both files already use. A row for an ID that doesn't exist is reported as an orphan, and so is a rule page or spec folder with no matching rule.

The CHANGELOG and release-tracking checks require only that the ID appears, because their formats vary between released and unreleased sections.

### Tracing becomes mandatory once a capability is first traced

The trace check parses every spec's scenario names, grouped by capability, and every `Spec` trait in `tests/**/*.cs`. It fails on:

- any trait naming an unknown capability or scenario
- any duplicate scenario name within a traced capability
- any scenario of a traced capability that no trait declares

A capability becomes traced automatically when any test first tags one of its scenarios. This needs no configuration file and can't be half-applied by mistake.

A configured list of traced capabilities was rejected, because it is one more place that can drift.

EFD023 is traced first. Its scenarios are tagged on the existing analyzer and end-to-end tests. Theories can carry several traits, one per scenario their rows cover.

### Remove per-rule CLI requirements instead of backfilling them

Backfilling 10 missing "Run EFDNNN" requirements would repeat the same text again, and every future rule would have to remember it. One general requirement in `cli-analysis`, backed by the registration check, specifies the behavior once and enforces it mechanically. Rule-specific CLI evidence stays in each rule's end-to-end test.

## Risks / Trade-offs

- **Text checks break when the README is restructured.** → The checks match the table and range formats the README already uses. A restructure updates the test in the same change, and the failure message says what the test looked for.
- **Reflecting over a private field couples the test to `WorkspaceAnalyzer` internals.** → If the field is renamed, the test fails loudly, and the fix is one line. This is preferable to exposing registration publicly just for tests.
- **Tracing EFD023 means many trait attributes.** → That's accepted as the cost of proving the mechanism on a real rule. Other rules opt in gradually.
- **The other session may add a rule while this change is in flight.** → The checks apply to every rule, so that session's next test run tells it exactly what to add. The claim for this change is pushed before implementation.

## Migration Plan

Land the change with the specs synced. Future rules must satisfy the checks, and `CLAUDE.md` says so. To roll back, delete the test class and restore the removed `cli-analysis` requirements from the archived delta. No runtime behavior, report field, or JSON schema changes.
