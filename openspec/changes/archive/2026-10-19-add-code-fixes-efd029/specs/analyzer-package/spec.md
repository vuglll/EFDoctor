## MODIFIED Requirements

### Requirement: Distribute the rules as an analyzer package
The repository SHALL produce a NuGet package with ID `EFDoctor.Analyzers` that carries the analyzer assembly and the `EFDoctor.CodeFixes` code-fix assembly under `analyzers/dotnet/cs/` and no `lib` assets. The package SHALL be marked as a development dependency and SHALL declare no package dependencies. It SHALL carry the same version as the `EFDoctor` tool package, the Apache-2.0 license expression, a package readme, the icon, `NOTICE`, and the changelog, and SHALL embed repository and project URLs only when the `EFDoctorRepositoryUrl` build property is set. Referencing the package SHALL run every shipped rule during compilation, including when the reference and its version come from central package management.

#### Scenario: Package contents
- **WHEN** the analyzer package is inspected
- **THEN** it contains `analyzers/dotnet/cs/EFDoctor.Analyzers.dll`, `analyzers/dotnet/cs/EFDoctor.CodeFixes.dll`, `build/EFDoctor.Analyzers.props`, the readme, the icon, `NOTICE`, and the changelog, has no `lib` folder and no dependencies, is a development dependency, and has the tool's version

#### Scenario: Referenced through central package management
- **WHEN** a project gets `EFDoctor.Analyzers` from a `PackageReference` in `Directory.Build.props` with its version in `Directory.Packages.props`, and its code calls `SaveChanges` inside a loop
- **THEN** building the project reports an `EFD001` warning
