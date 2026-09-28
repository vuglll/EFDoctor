## ADDED Requirements

### Requirement: Warn about EF Core projects that cannot be analyzed
For each C# project it analyzes, the CLI SHALL determine whether the project uses EF Core and whether EF Core types resolve in its compilation. It SHALL NOT make this check for a test project that it skips.

A project uses EF Core when any of its source files has a `using` directive, including a `global using`, for `Microsoft.EntityFrameworkCore` or one of its sub-namespaces.

A project that uses EF Core, but whose compilation cannot resolve `Microsoft.EntityFrameworkCore.DbContext`, is unanalyzable. For each unanalyzable project, the CLI SHALL write a warning to standard error, prefixed `EFDoctor warning:`. The warning SHALL:
- name the project;
- state that EF Core types could not be resolved, so the project was not analyzed;
- recommend restoring and building the project;
- include up to three workspace load diagnostics that refer to the project, when there are any.

Warnings SHALL be written in console and JSON modes alike, and regardless of `--quiet`. They SHALL NOT be written to standard output. The JSON report schema SHALL NOT change.

#### Scenario: Unanalyzable EF project alongside analyzable ones
- **WHEN** a solution contains a restored EF Core project with findings and an unrestored project that uses EF Core
- **THEN** the CLI reports the restored project's findings with its normal exit code, and writes a warning naming the unrestored project to standard error

#### Scenario: JSON output stays a pure report
- **WHEN** the same solution is analyzed in JSON mode
- **THEN** standard output contains only the parseable JSON report, and the warning appears on standard error

#### Scenario: Project without EF Core usage is not flagged
- **WHEN** an analyzed project has no `using` directive for EF Core, whether or not it is restored
- **THEN** the CLI writes no EF resolution warning for it

#### Scenario: Resolved EF project produces no warning
- **WHEN** a restored project that uses EF Core is analyzed
- **THEN** the CLI writes nothing to standard error

#### Scenario: Skipped test project is not checked
- **WHEN** a test project is skipped because `--include-test-projects` was not given
- **THEN** the CLI does not check it and writes no warning for it

### Requirement: Fail when no EF Core project can be analyzed
When the target contains at least one analyzed project that uses EF Core, and none of those projects is analyzable, the CLI SHALL report an analysis failure with exit code `2` instead of a successful result. The failure message SHALL name each unanalyzable project, state that EF Core types could not be resolved, and recommend restoring and building. It SHALL be emitted like any other analysis failure: on standard error in console mode, and as the JSON error envelope with code `analysis-failure` in JSON mode.

#### Scenario: Unrestored EF-only project
- **WHEN** the target is a single project that uses EF Core and has not been restored
- **THEN** the CLI exits with code `2`, and its error names the project and recommends restoring it, instead of reporting no findings

#### Scenario: Unrestored EF-only project in JSON mode
- **WHEN** the same project is analyzed in JSON mode
- **THEN** standard output contains the JSON error envelope with code `analysis-failure`, and the process exits with code `2`
