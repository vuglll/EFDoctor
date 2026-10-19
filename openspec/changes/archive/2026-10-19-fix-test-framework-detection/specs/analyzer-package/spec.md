## MODIFIED Requirements

### Requirement: Skip test projects in builds
The analyzers SHALL report nothing for a test project. A project is a test project when its `IsTestProject` MSBuild property is `true`, or when its compilation references a recognized test framework: a framework's core assembly: `xunit`, `xunit.core`, an assembly whose name starts with `xunit.v3.core` or `Microsoft.VisualStudio.TestPlatform`, `nunit.framework`, or `Microsoft.NET.Test.Sdk`. A library that only ships alongside a framework, such as `xunit.assert` or `xunit.abstractions`, SHALL NOT make a project a test project. The package SHALL make `IsTestProject` visible to the compiler. Setting the MSBuild property `EFDoctorAnalyzeTestProjects` to `true` SHALL opt a test project back in, with the same severities as any other project.

#### Scenario: Project marked as a test project
- **WHEN** a compilation with a finding has `build_property.IsTestProject` set to `true`
- **THEN** no EFDoctor diagnostic is reported

#### Scenario: Test-framework reference
- **WHEN** a compilation with a finding references the `xunit.core` assembly, and `IsTestProject` is not set
- **THEN** no EFDoctor diagnostic is reported

#### Scenario: Assertion library without a test framework
- **WHEN** a compilation with a finding references `xunit.assert` or `xunit.abstractions` but no test framework's core assembly, and `IsTestProject` is not set
- **THEN** its findings are reported

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
