# Spec Delta

## ADDED Requirements

### Requirement: Distribute the CLI as a .NET tool package
The CLI SHALL be packable as a .NET tool package with package ID `EFDoctor` and command name `efdoctor` that installs with the standard `dotnet tool` global and local-manifest workflows and runs on the .NET 10 runtime or a later major runtime. The package SHALL carry its version, description, tags, icon, package readme, release notes, the Apache-2.0 license expression, the `NOTICE` file, and third-party notices for bundled dependencies, and SHALL be accompanied by a symbols package. While the source repository is private, the package SHALL NOT embed repository or project URLs. The package SHALL include the components the CLI needs to load projects and SHALL NOT bundle MSBuild runtime assemblies that must come from the installed .NET SDK.

#### Scenario: Install from a local package source
- **WHEN** a user packs the CLI and installs the resulting package into a tool path from a local package source
- **THEN** the installed `efdoctor` command runs and reports the packaged version

#### Scenario: Analyze with the installed tool
- **WHEN** the installed `efdoctor` command analyzes a build-ready project containing an EFDoctor finding
- **THEN** it produces the same findings and exit code as the repository-built CLI

#### Scenario: Package contents
- **WHEN** the package is inspected
- **THEN** it contains the license expression, `NOTICE`, third-party notices, package readme, and icon, contains no repository URL, and contains no MSBuild runtime assemblies

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
The CLI SHALL select the .NET SDK used to load a target by resolving SDKs from the target's own directory, so that a `global.json` governing the target is honored regardless of the directory from which the CLI is run. When no installed SDK satisfies the target's resolution, the CLI SHALL report an analysis failure that names the target directory and explains that a matching .NET SDK must be installed, and SHALL NOT silently load the target with a different SDK.

#### Scenario: Target without global.json
- **WHEN** a user analyzes a target whose directory tree has no `global.json`
- **THEN** the CLI loads it with the default installed SDK, as before

#### Scenario: Target pins an installed SDK
- **WHEN** a user analyzes a target whose `global.json` selects an installed SDK and runs the CLI from a different directory
- **THEN** the CLI resolves the SDK from the target's directory rather than the working directory

#### Scenario: Target pins a missing SDK
- **WHEN** a user analyzes a target whose `global.json` requires an SDK that is not installed
- **THEN** the CLI exits with code `2` and an error explaining that no matching .NET SDK was found for the target directory

### Requirement: Document tool installation
The repository SHALL document how to pack the CLI locally, install it as a global or local tool from a local package source, run it, update it, and uninstall it, and SHALL state the runtime, SDK, restore, and trust prerequisites for analyzed targets.

#### Scenario: Tester installs a preview
- **WHEN** a tester follows the documented local pack and install steps on a machine with a supported .NET SDK
- **THEN** the `efdoctor` command is available and can analyze a restored project
