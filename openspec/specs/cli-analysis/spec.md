# CLI Analysis Specification

## Purpose

Defines the local EFDoctor command-line scan experience for loading a supplied .NET solution or project, running analysis, and producing automation-safe results.

## Requirements

### Requirement: Analyze a supplied solution or project
The CLI SHALL provide an analysis command that accepts a path to an existing supported .NET solution or project and runs the included EFDoctor analyzers against its C# compilations. Workspace load diagnostics reported while opening the target SHALL NOT by themselves fail the analysis: the CLI SHALL analyze every C# project for which a compilation is available. The CLI SHALL report an analysis failure only when the target produces no analyzable C# compilation, and that failure message SHALL include the collected workspace load diagnostics.

#### Scenario: Analyze a solution
- **WHEN** a user supplies a valid supported solution path
- **THEN** the CLI loads its analyzable C# projects and reports their EFDoctor findings

#### Scenario: Analyze a project
- **WHEN** a user supplies a valid supported C# project path
- **THEN** the CLI loads that project and reports its EFDoctor findings

#### Scenario: Target loads with non-fatal build warnings
- **WHEN** a target opens with workspace load diagnostics but still produces at least one C# compilation
- **THEN** the CLI analyzes the available compilations, reports their findings, and does not fail solely because of the load diagnostics

#### Scenario: Target produces no compilation
- **WHEN** opening the target yields no analyzable C# compilation
- **THEN** the CLI reports an analysis failure whose message includes the collected workspace load diagnostics and returns the analysis-failure exit code

#### Scenario: Missing path
- **WHEN** the analysis command is invoked without a required target path
- **THEN** the CLI prints a clear usage or validation error and returns the invalid-input exit code

#### Scenario: Invalid or unsupported path
- **WHEN** the supplied path does not exist, has an unsupported type, or cannot be loaded for analysis
- **THEN** the CLI identifies the input problem without emitting a misleading successful report and returns the invalid-input or analysis-failure exit code as applicable

### Requirement: Select console or JSON output
The CLI SHALL default to human-readable console output and SHALL provide an option for machine-readable JSON output. It SHALL also provide quiet and/or no-color behavior that prevents decorative output and ANSI color sequences from interfering with automation.

#### Scenario: Default console mode
- **WHEN** a user runs analysis without selecting JSON
- **THEN** the CLI emits the human-readable report

#### Scenario: JSON mode
- **WHEN** a user selects JSON output
- **THEN** standard output contains only the versioned JSON report and no banners, progress text, or ANSI control sequences

#### Scenario: No-color automation mode
- **WHEN** a user disables color
- **THEN** all output omits ANSI color sequences while preserving result content

#### Scenario: Quiet automation mode
- **WHEN** a user selects quiet mode
- **THEN** nonessential progress and decorative messages are omitted while errors, the requested report, and the process exit code remain available

### Requirement: Return stable exit codes
The CLI SHALL return exit code `0` when analysis succeeds with no findings, `1` when analysis succeeds with one or more findings, and `2` for invalid input or an analysis failure. These meanings SHALL be documented and remain stable for schema version `1` consumers.

#### Scenario: Successful clean scan
- **WHEN** analysis completes successfully with no findings
- **THEN** the process exits with code `0`

#### Scenario: Successful scan with findings
- **WHEN** analysis completes successfully with one or more findings
- **THEN** the process exits with code `1`

#### Scenario: Invalid input or analysis failure
- **WHEN** input validation fails or the target cannot be analyzed successfully
- **THEN** the process exits with code `2` and emits a clear error suitable for the selected output mode

### Requirement: Keep analysis local and private
The CLI and analyzer SHALL make no product-initiated network requests and SHALL NOT transmit source code, findings, paths, telemetry, or usage data. Reading locally available project inputs and dependencies SHALL be the only data access needed after the target is build-ready on the machine.

#### Scenario: Analyze a fixture with network unavailable
- **WHEN** required SDKs and dependencies are already available locally and network access is unavailable
- **THEN** EFDoctor completes analysis without attempting telemetry, source upload, license validation, or any other product network call

### Requirement: Document developer operation
The repository SHALL provide concise commands for restoring prerequisites, building from a clean checkout, running all tests, invoking the CLI locally, selecting console or JSON output, interpreting exit codes, and suppressing an intentional EFD001 finding with a recorded justification.

#### Scenario: New contributor follows documentation
- **WHEN** a contributor has the documented supported .NET SDK and follows the repository instructions from a clean checkout
- **THEN** the solution builds, the full automated test suite runs, and the fixture analysis command can be executed

### Requirement: Distribute the CLI as a .NET tool package
The CLI SHALL be packable as a .NET tool package with package ID `EFDoctor` and command name `efdoctor` that installs with the standard `dotnet tool` global and local-manifest workflows and runs on the .NET 10 runtime or a later major runtime. The package SHALL carry its version, description, tags, icon, package readme, release notes, the Apache-2.0 license expression, the `NOTICE` file, and third-party notices for bundled dependencies, and SHALL be accompanied by a symbols package. The package SHALL embed repository and project URLs, and Source Link, only when the `EFDoctorRepositoryUrl` build property is set; by default it SHALL embed none. The package SHALL include the components the CLI needs to load projects and SHALL NOT bundle MSBuild runtime assemblies that must come from the installed .NET SDK.

#### Scenario: Install from a local package source
- **WHEN** a user packs the CLI and installs the resulting package into a tool path from a local package source
- **THEN** the installed `efdoctor` command runs and reports the packaged version

#### Scenario: Analyze with the installed tool
- **WHEN** the installed `efdoctor` command analyzes a build-ready project containing an EFDoctor finding
- **THEN** it produces the same findings and exit code as the repository-built CLI

#### Scenario: Package contents
- **WHEN** the package is inspected
- **THEN** it contains the license expression, `NOTICE`, third-party notices, package readme, and icon, contains no repository URL when `EFDoctorRepositoryUrl` is unset, and contains no MSBuild runtime assemblies

### Requirement: Report version and usage information
The CLI SHALL print its informational version for `--version` and usage help for `--help` or `-h`, each to standard output with exit code `0`, without loading or analyzing any project. Invoking the CLI with no arguments or an unknown command SHALL continue to print usage as an invalid-input error with exit code `2`.

#### Scenario: Version output
- **WHEN** a user runs `efdoctor --version`
- **THEN** the CLI prints the tool version and exits with code `0`

#### Scenario: Help output
- **WHEN** a user runs `efdoctor --help` or `efdoctor -h`
- **THEN** the CLI prints the command shape, options, and exit-code meanings and exits with code `0`

#### Scenario: Missing command
- **WHEN** a user runs `efdoctor` with no arguments
- **THEN** the CLI prints usage as an error and exits with code `2`

### Requirement: Load targets with the SDK selected for the target
The CLI SHALL select the .NET SDK used to load a target by resolving SDKs from the target's own directory, so that a `global.json` governing the target is honored regardless of the directory from which the CLI is run. When no installed SDK satisfies the target's resolution, the CLI SHALL report an analysis failure that names the target directory and explains that a matching .NET SDK must be installed, and SHALL NOT silently load the target with a different SDK. Resolving the SDK SHALL NOT write anything to the CLI's standard output, so that in JSON mode standard output holds only the error envelope.

#### Scenario: Target without global.json
- **WHEN** a user analyzes a target whose directory tree has no `global.json`
- **THEN** the CLI loads it with the default installed SDK, as before

#### Scenario: Target pins an installed SDK
- **WHEN** a user analyzes a target whose `global.json` selects an installed SDK and runs the CLI from a different directory
- **THEN** the CLI resolves the SDK from the target's directory rather than the working directory

#### Scenario: Target pins a missing SDK
- **WHEN** a user analyzes a target whose `global.json` requires an SDK that is not installed
- **THEN** the CLI exits with code `2` and an error explaining that no matching .NET SDK was found for the target directory

#### Scenario: Missing SDK in JSON mode keeps standard output pure
- **WHEN** the CLI process analyzes, in JSON mode, a target whose `global.json` requires an SDK that is not installed
- **THEN** the process's standard output parses as exactly one JSON document, the error envelope with code `analysis-failure`, and contains no installed-SDK list

### Requirement: Document tool installation
The repository SHALL document how to pack the CLI locally, install it as a global or local tool from a local package source, run it, update it, and uninstall it, and SHALL state the runtime, SDK, restore, and trust prerequisites for analyzed targets.

#### Scenario: Tester installs a preview
- **WHEN** a tester follows the documented local pack and install steps on a machine with a supported .NET SDK
- **THEN** the `efdoctor` command is available and can analyze a restored project

### Requirement: Run every shipped analyzer during workspace analysis
The CLI SHALL run every EFDoctor analyzer that the analyzer assembly ships against the C# compilations of the supplied target. Findings from every rule SHALL go through the same reporting, ordering, suppression, privacy, test-project, and exit-code contracts, and SHALL keep the severity and confidence their rule assigns except where those shared contracts change them. Each rule's detection behavior is specified in that rule's own capability spec, not in this capability.

#### Scenario: Analyze a project with an unsuppressed finding from any rule
- **WHEN** a user analyzes a supported production project that contains an unsuppressed match for any shipped rule
- **THEN** the CLI reports that finding with its rule-assigned severity and confidence in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze a project with only suppressed or non-matching code
- **WHEN** every candidate pattern in the target falls outside its rule's boundary or is protected by standard Roslyn suppression
- **THEN** those patterns add no finding to the CLI report

#### Scenario: A newly shipped rule
- **WHEN** a new analyzer is added to the analyzer assembly
- **THEN** it runs during workspace analysis without any rule-specific change to this capability

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
