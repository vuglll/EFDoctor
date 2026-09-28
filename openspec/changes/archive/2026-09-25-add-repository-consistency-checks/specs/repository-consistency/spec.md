# Spec Delta

## Purpose

Defines the automated checks that keep EFDoctor's analyzers, CLI registration, specs, rule documentation, release metadata, solution file, and scenario-level test coverage in sync, so parallel contributors cannot silently leave any of them behind.

## ADDED Requirements

### Requirement: Run consistency checks with the automated test suite
The repository consistency checks SHALL run as ordinary tests in the automated test suite, so the standard `dotnet test` command enforces them locally and in any CI pipeline without extra tooling. A failing check SHALL name the rule, file, or scenario that is out of sync.

#### Scenario: Running the test suite
- **WHEN** a contributor runs the automated test suite
- **THEN** the consistency checks run alongside the analyzer and CLI tests

#### Scenario: A check fails
- **WHEN** a consistency check finds a problem
- **THEN** the failure message names each missing or orphaned item, such as the rule ID and the file or section it is missing from

### Requirement: Register and document every shipped rule
For every diagnostic ID that the analyzer assembly exports, the repository SHALL contain all of the following:

- the rule's registration in the CLI analyzer set
- a rule capability spec under `openspec/specs/` whose folder name starts with the lowercase rule ID
- a rule page at `docs/rules/<ID>.md`
- a row in the README rule table
- coverage in the README project status line
- coverage in the README verification map's analyzer-test list
- a row in the package readme rule table
- a release-tracking entry
- an entry in the CHANGELOG

A rule ID is covered by the status line or verification list when it is named directly or falls inside a stated range such as "EFD011 through EFD014".

#### Scenario: A new rule missing a documentation location
- **WHEN** an analyzer is added but one of the required locations does not mention its rule ID
- **THEN** the consistency check fails and names the rule and the missing location

#### Scenario: A rule covered by a range
- **WHEN** the README status line says "EFD011 through EFD014"
- **THEN** EFD011, EFD012, EFD013, and EFD014 all count as covered

#### Scenario: A shipped analyzer not registered in the CLI
- **WHEN** the analyzer assembly exports a diagnostic ID that no analyzer in the CLI analyzer set supports
- **THEN** the consistency check fails and names the rule

### Requirement: Reject orphaned rule artifacts
The repository SHALL NOT contain a rule page, a rule capability spec, a README rule-table row, or a package-readme rule-table row for a rule ID that the analyzer assembly does not export.

#### Scenario: Documentation for a rule that does not exist
- **WHEN** `docs/rules/` contains a page for a rule ID that no analyzer exports
- **THEN** the consistency check fails and names the orphaned page

### Requirement: Keep the solution structure consistent
The solution file SHALL contain exactly one "Fixtures" solution folder, and every fixture project under `tests/Fixtures` SHALL be included in the solution. A fixture that marks itself as a test project (`IsTestProject` set to `true`) SHALL stay out of the solution, because the solution-wide test run would otherwise try to execute it as a test assembly.

#### Scenario: Duplicate Fixtures folder
- **WHEN** adding a fixture creates a second "Fixtures" solution folder
- **THEN** the consistency check fails

#### Scenario: Fixture missing from the solution
- **WHEN** a project exists under `tests/Fixtures` but is not listed in the solution
- **THEN** the consistency check fails and names the project

#### Scenario: Test-project fixture outside the solution
- **WHEN** a fixture under `tests/Fixtures` sets `IsTestProject` to `true` and is not listed in the solution
- **THEN** the consistency check does not report it

### Requirement: Trace spec scenarios to tests
A test SHALL declare each spec scenario it covers with a trait named `Spec` whose value is `<capability>/<scenario name>`, where the capability is the spec folder name under `openspec/specs/`. Every such trait SHALL name an existing capability and an existing scenario in it. A capability becomes traced as soon as any test declares one of its scenarios. From then on, every scenario in that capability SHALL be declared by at least one test. Scenario names SHALL be unique within a traced capability.

#### Scenario: A test cites a scenario that does not exist
- **WHEN** a test's `Spec` trait names a capability or scenario that is not in the specs
- **THEN** the consistency check fails and names the trait and the test file

#### Scenario: A traced capability with an untested scenario
- **WHEN** a capability is traced and one of its scenarios is not declared by any test
- **THEN** the consistency check fails and names the scenario

#### Scenario: A capability that is not yet traced
- **WHEN** no test declares any scenario of a capability
- **THEN** the scenario-coverage check does not apply to that capability
