# Spec Delta

## Purpose

Apply a single, rule-agnostic downgrade so that any EFDoctor finding originating in a test project is reported at advisory confidence and informational severity, for every rule present and future.

## ADDED Requirements

### Requirement: Downgrade findings from test projects
Workspace analysis SHALL classify each analyzed project as a test project or a production project, and SHALL downgrade every EFDoctor finding originating in a test project to advisory confidence and `Info` severity before rendering, regardless of the rule that produced it. A project is a test project when it references a recognized test framework — including `xunit`, `nunit.framework`, `Microsoft.VisualStudio.TestPlatform`, or `Microsoft.NET.Test.Sdk` — or is marked with the MSBuild `IsTestProject` property. The downgrade SHALL apply uniformly to all current and future rules without per-rule code, SHALL NOT silently suppress a finding so that a genuine issue in test code remains discoverable and configurable, and SHALL NOT change the finding contract or the JSON schema version.

#### Scenario: Test project finding downgraded
- **WHEN** any EFDoctor rule produces a finding in a project that references a recognized test framework
- **THEN** the rendered finding has advisory confidence and `Info` severity

#### Scenario: Production project finding unchanged
- **WHEN** the same rule produces a finding in a project with no test-framework reference and no `IsTestProject` marker
- **THEN** the finding retains its rule-assigned confidence and severity

#### Scenario: Downgrade applies to every rule
- **WHEN** findings from more than one rule originate in a test project
- **THEN** each is downgraded uniformly without rule-specific handling

#### Scenario: Downgrade never suppresses
- **WHEN** a finding is downgraded because it originates in a test project
- **THEN** it is still reported and still counts toward the successful-scan-with-findings exit code, and it can be suppressed only through standard Roslyn suppression

#### Scenario: Schema and contract unchanged
- **WHEN** the test-project downgrade is applied
- **THEN** JSON output retains schema version `1` and every required finding field
