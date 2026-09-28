# Spec Delta

## ADDED Requirements

### Requirement: Skip test projects unless explicitly included
By default the CLI SHALL NOT analyze projects classified as test projects: a skipped test project produces no findings and does not affect the exit code. The CLI SHALL provide an `--include-test-projects` flag that opts test projects back into analysis; when set, their findings are produced and downgraded per "Downgrade findings from test projects". Project classification is unchanged (a recognized test-framework reference or the MSBuild `IsTestProject` property). A target whose only analyzable projects are skipped test projects SHALL be reported as a successful scan with no findings, not as an analysis failure.

#### Scenario: Test project skipped by default
- **WHEN** a target contains a test project and `--include-test-projects` is not supplied
- **THEN** the CLI does not analyze that project and reports no findings originating from it

#### Scenario: Include test projects on request
- **WHEN** `--include-test-projects` is supplied and a test project contains an EFDoctor finding
- **THEN** the CLI analyzes the test project and reports the finding at advisory confidence and `Info` severity

#### Scenario: Production projects unaffected by the flag
- **WHEN** a solution mixes production and test projects
- **THEN** production-project findings are reported with their rule-assigned confidence and severity whether or not `--include-test-projects` is supplied

#### Scenario: Only test projects present
- **WHEN** every analyzable project in the target is a test project and `--include-test-projects` is not supplied
- **THEN** the CLI reports a successful scan with no findings and the no-findings exit code, not an analysis failure

## MODIFIED Requirements

### Requirement: Downgrade findings from test projects
When test projects are included in analysis via `--include-test-projects` (see "Skip test projects unless explicitly included"), workspace analysis SHALL classify each analyzed project as a test project or a production project, and SHALL downgrade every EFDoctor finding originating in an included test project to advisory confidence and `Info` severity before rendering, regardless of the rule that produced it. A project is a test project when it references a recognized test framework — including `xunit`, `nunit.framework`, `Microsoft.VisualStudio.TestPlatform`, or `Microsoft.NET.Test.Sdk` — or is marked with the MSBuild `IsTestProject` property. The downgrade SHALL apply uniformly to all current and future rules without per-rule code, SHALL NOT silently suppress an included finding so that a genuine issue in test code remains discoverable and configurable, and SHALL NOT change the finding contract or the JSON schema version. By default test projects are not analyzed, so no such findings are produced.

#### Scenario: Test project finding downgraded
- **WHEN** `--include-test-projects` is supplied and any EFDoctor rule produces a finding in a project that references a recognized test framework
- **THEN** the rendered finding has advisory confidence and `Info` severity

#### Scenario: Production project finding unchanged
- **WHEN** the same rule produces a finding in a project with no test-framework reference and no `IsTestProject` marker
- **THEN** the finding retains its rule-assigned confidence and severity

#### Scenario: Downgrade applies to every rule
- **WHEN** `--include-test-projects` is supplied and findings from more than one rule originate in a test project
- **THEN** each is downgraded uniformly without rule-specific handling

#### Scenario: Downgrade never suppresses
- **WHEN** a finding is downgraded because it originates in an included test project
- **THEN** it is still reported and still counts toward the successful-scan-with-findings exit code, and it can be suppressed only through standard Roslyn suppression

#### Scenario: Schema and contract unchanged
- **WHEN** the test-project downgrade is applied
- **THEN** JSON output retains schema version `1` and every required finding field
