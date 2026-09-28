# Spec Delta

## MODIFIED Requirements

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
