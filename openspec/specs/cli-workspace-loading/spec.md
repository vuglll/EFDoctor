# cli-workspace-loading Specification

## Purpose
Defines how the CLI works around MSBuildWorkspace load limitations, so that projects `dotnet build` accepts are analyzed rather than reported as unanalyzable.

## Requirements

### Requirement: Load multi-targeted projects whose framework list contains whitespace
The CLI SHALL load a multi-targeted project whose `TargetFrameworks` value, after property evaluation, has whitespace or line breaks around or between its entries, or empty entries. It SHALL analyze the project exactly as if the list had been written on one line without whitespace. The CLI SHALL NOT modify any file in the analyzed repository to do so. Single-targeted projects SHALL load as before.

#### Scenario: Framework list spans several lines
- **WHEN** a restored project that uses EF Core sets `TargetFrameworks` to a value that has one target framework on its own indented line
- **THEN** the CLI analyzes the project and reports its findings, and writes no EF resolution warning

#### Scenario: Framework list with several entries separated by line breaks
- **WHEN** a restored project sets `TargetFrameworks` to two target frameworks separated by `;` and line breaks, with a trailing `;`
- **THEN** the CLI analyzes the project for its target frameworks and reports its findings once per location
