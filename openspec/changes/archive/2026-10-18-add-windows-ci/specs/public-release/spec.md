## MODIFIED Requirements

### Requirement: Run the test suite in continuous integration
The repository SHALL include a GitHub Actions workflow that restores, builds, and runs the full test suite with the .NET 10 SDK, on Linux and on Windows, on every push and pull request.

#### Scenario: Pull request
- **WHEN** a pull request is opened or updated
- **THEN** the workflow builds the solution with warnings as errors and runs every test project, on both Linux and Windows

#### Scenario: Required check name is stable
- **WHEN** the Windows job is added
- **THEN** the Linux job keeps the check name `build-and-test`, which branch protection requires
