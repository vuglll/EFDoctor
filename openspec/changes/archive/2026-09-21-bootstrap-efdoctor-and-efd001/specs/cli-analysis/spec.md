# Spec Delta

## Purpose

Defines the local EFDoctor command-line scan experience for loading a supplied .NET solution or project, running analysis, and producing automation-safe results.

## ADDED Requirements

### Requirement: Analyze a supplied solution or project
The CLI SHALL provide an analysis command that accepts a path to an existing supported .NET solution or project and runs the included EFDoctor analyzers against its C# compilations.

#### Scenario: Analyze a solution
- **WHEN** a user supplies a valid supported solution path
- **THEN** the CLI loads its analyzable C# projects and reports their EFDoctor findings

#### Scenario: Analyze a project
- **WHEN** a user supplies a valid supported C# project path
- **THEN** the CLI loads that project and reports its EFDoctor findings

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
