## MODIFIED Requirements

### Requirement: Downgrade findings from test projects
When test projects are included in analysis via `--include-test-projects` (see "Skip test projects unless explicitly included"), workspace analysis SHALL classify each analyzed project as a test project or a production project, and SHALL downgrade every EFDoctor finding originating in an included test project to advisory confidence and `Info` severity before rendering, regardless of the rule that produced it. A project is a test project when it references a recognized test framework — a framework's core assembly, such as `xunit.core`, `xunit.v3.core`, `nunit.framework`, `Microsoft.VisualStudio.TestPlatform.TestFramework`, or `Microsoft.NET.Test.Sdk`, and not an assertion library such as `xunit.assert` — or is marked with the MSBuild `IsTestProject` property. The downgrade SHALL apply uniformly to all current and future rules without per-rule code, SHALL NOT silently suppress an included finding so that a genuine issue in test code remains discoverable and configurable, and SHALL NOT change the finding contract or the JSON schema version. By default test projects are not analyzed, so no such findings are produced.

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
