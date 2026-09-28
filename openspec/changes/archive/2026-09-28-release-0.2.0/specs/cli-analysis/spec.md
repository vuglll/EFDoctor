## MODIFIED Requirements

### Requirement: Document tool installation
The repository SHALL document how to install the CLI from nuget.org as a global or local tool, how to pack it from source and install it from a local package source, and how to run, update, and uninstall it, and SHALL state the runtime, SDK, restore, and trust prerequisites for analyzed targets.

#### Scenario: Tester installs a preview
- **WHEN** a tester follows the documented local pack and install steps on a machine with a supported .NET SDK
- **THEN** the `efdoctor` command is available and can analyze a restored project

#### Scenario: User installs a release
- **WHEN** a user runs the documented `dotnet tool install --global EFDoctor` on a machine with a supported .NET runtime
- **THEN** the latest release from nuget.org is installed, and `efdoctor --version` reports it
