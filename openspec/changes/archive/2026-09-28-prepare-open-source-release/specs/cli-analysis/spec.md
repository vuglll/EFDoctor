## MODIFIED Requirements

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
