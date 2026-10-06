# analyzer-package Specification

## Purpose
Defines the `EFDoctor.Analyzers` NuGet package, which runs EFDoctor's rules inside the compiler: in every build and in the IDE.

## Requirements

### Requirement: Distribute the rules as an analyzer package
The repository SHALL produce a NuGet package with ID `EFDoctor.Analyzers` that carries the analyzer assembly under `analyzers/dotnet/cs/` and no `lib` assets. The package SHALL be marked as a development dependency and SHALL declare no package dependencies. It SHALL carry the same version as the `EFDoctor` tool package, the Apache-2.0 license expression, a package readme, the icon, `NOTICE`, and the changelog, and SHALL embed repository and project URLs only when the `EFDoctorRepositoryUrl` build property is set. Referencing the package SHALL run every shipped rule during compilation, including when the reference and its version come from central package management.

#### Scenario: Package contents
- **WHEN** the analyzer package is inspected
- **THEN** it contains `analyzers/dotnet/cs/EFDoctor.Analyzers.dll`, `build/EFDoctor.Analyzers.props`, the readme, the icon, `NOTICE`, and the changelog, has no `lib` folder and no dependencies, is a development dependency, and has the tool's version

#### Scenario: Referenced through central package management
- **WHEN** a project gets `EFDoctor.Analyzers` from a `PackageReference` in `Directory.Build.props` with its version in `Directory.Packages.props`, and its code calls `SaveChanges` inside a loop
- **THEN** building the project reports an `EFD001` warning

### Requirement: Load in the .NET 8 SDK compiler and later
The analyzer assembly SHALL target `netstandard2.0` and SHALL reference Roslyn (`Microsoft.CodeAnalysis`) no newer than version 4.8, so that the compiler of the .NET 8 SDK, and any later compiler, loads it.

#### Scenario: Roslyn baseline
- **WHEN** the built analyzer assembly's references are read
- **THEN** its `Microsoft.CodeAnalysis` reference has version 4.8 or lower

### Requirement: Default build severity follows confidence
A diagnostic's default severity in a build SHALL follow the confidence of its finding. A high-confidence finding SHALL have its rule's default severity. A medium-confidence or advisory finding SHALL have at most `Info` severity (a suggestion), whatever its rule's default. A rule whose findings never exceed medium confidence SHALL declare `Info` as its default severity. A severity configured for the rule's ID, for example in `.editorconfig`, SHALL replace the default for every finding of that rule.

#### Scenario: High-confidence finding is a warning
- **WHEN** a rule with `Warning` default severity reports a high-confidence finding
- **THEN** the diagnostic has `Warning` severity

#### Scenario: Medium-confidence finding is a suggestion
- **WHEN** a rule reports a medium-confidence finding
- **THEN** the diagnostic has `Info` severity

#### Scenario: Rule with several confidence levels
- **WHEN** EFD005 reports one high-confidence, one medium-confidence, and one advisory finding
- **THEN** the first diagnostic has `Warning` severity, and the other two have `Info` severity

#### Scenario: Medium-only rules default to Info
- **WHEN** the descriptors of EFD006, EFD009, EFD010, EFD013, and EFD020 are read
- **THEN** each declares `Info` as its default severity

#### Scenario: Configured severity replaces the default
- **WHEN** a medium-confidence rule's ID is configured to `warning`
- **THEN** its diagnostics have `Warning` severity

#### Scenario: Suggestions do not fail a build
- **WHEN** a project that references the package contains only a medium-confidence finding
- **THEN** the build reports no EFDoctor warning, and its diagnostic log holds the finding as a note

### Requirement: Skip test projects in builds
The analyzers SHALL report nothing for a test project. A project is a test project when its `IsTestProject` MSBuild property is `true`, or when its compilation references a recognized test framework: an assembly whose name starts with `xunit` or `Microsoft.VisualStudio.TestPlatform`, or that is named `nunit.framework` or `Microsoft.NET.Test.Sdk`. The package SHALL make `IsTestProject` visible to the compiler. Setting the MSBuild property `EFDoctorAnalyzeTestProjects` to `true` SHALL opt a test project back in, with the same severities as any other project.

#### Scenario: Project marked as a test project
- **WHEN** a compilation with a finding has `build_property.IsTestProject` set to `true`
- **THEN** no EFDoctor diagnostic is reported

#### Scenario: Test-framework reference
- **WHEN** a compilation with a finding references an xunit assembly, and `IsTestProject` is not set
- **THEN** no EFDoctor diagnostic is reported

#### Scenario: Opt back in
- **WHEN** a test project sets `build_property.EFDoctorAnalyzeTestProjects` to `true`
- **THEN** its findings are reported

#### Scenario: Production project
- **WHEN** a compilation has no test-framework reference, and `IsTestProject` is unset or `false`
- **THEN** its findings are reported

#### Scenario: Every analyzer checks the scope
- **WHEN** the shipped analyzers are inspected
- **THEN** each checks the analysis scope before registering any action

#### Scenario: Test project built with the package
- **WHEN** a project that sets `IsTestProject` to `true` references the package and calls `SaveChanges` inside a loop
- **THEN** its build reports no EFDoctor diagnostic, and reports `EFD001` once `EFDoctorAnalyzeTestProjects` is `true`

### Requirement: Link every rule to its documentation
Every rule's descriptor SHALL have a help link to that rule's reference page, `docs/rules/EFDNNN.md`, on the `main` branch of the repository named by `EFDoctorRepositoryUrl`.

#### Scenario: Help link
- **WHEN** the descriptor of any shipped rule is read
- **THEN** its help link is the repository URL followed by `/blob/main/docs/rules/` and the rule's page, and that page exists

### Requirement: Document the analyzer package
The README SHALL explain when to use the CLI and when to use the analyzer package, and the usage guide SHALL document installing the package, including through central package management, the default severities, how to change them in `.editorconfig`, the test-project behavior and its opt-in, and the oldest supported SDK.

#### Scenario: Reader chooses between the CLI and the package
- **WHEN** a reader opens the README and the usage guide
- **THEN** the README has a section comparing the CLI with the analyzer package, and the usage guide shows the `EFDoctor.Analyzers` package reference, a `dotnet_diagnostic` severity override, and the `EFDoctorAnalyzeTestProjects` property
