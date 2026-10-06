# Tasks

## 1. Analyzer behavior

- [ ] 1.1 Compile `EFDoctor.Analyzers` against Roslyn 4.8 (`VersionOverride`), replacing the one newer API in EFD027 with a match on the operation kind's value; verify the solution builds with no warnings, the existing EFD027 tests pass, and a test pins the value to `OperationKind.CollectionExpression`.
- [ ] 1.2 Add `EfDiagnostic.Create`, which caps medium and advisory findings at `Info`, use it in every analyzer, and change the descriptors of EFD006, EFD009, EFD010, EFD013, and EFD020 to `Info`; record the changes in `AnalyzerReleases.Unshipped.md`; verify tests for high, medium, mixed (EFD005), and configured severities.
- [ ] 1.3 Add `EfTestProjectDetection` and `EfAnalysisScope`, check the scope first in every analyzer's compilation-start action, and keep test-framework assemblies out of the analyzer test harness's default references; verify tests for the `IsTestProject` property, a test-framework reference, the opt-in, and a production project.
- [ ] 1.4 Add `EfHelpLinks` and a help link on every descriptor; verify a test over all descriptors.

## 2. CLI

- [ ] 2.1 Map a medium-confidence diagnostic at `Info` severity to a `Warning` finding in `DiagnosticFindingMapper`; verify mapper tests for medium, high, EFD025, and advisory, and that every end-to-end test still passes unchanged.
- [ ] 2.2 Use `EfTestProjectDetection` for the CLI's reference check, and pass the opt-in to the analyzers when `--include-test-projects` is set; verify the test-project end-to-end tests pass unchanged.

## 3. Packaging and release

- [ ] 3.1 Move `<Version>` to `Directory.Build.props`, and make `EFDoctor.Analyzers.csproj` packable as `EFDoctor.Analyzers` with its package readme and `build/EFDoctor.Analyzers.props`; verify `dotnet pack` succeeds with no warnings for both projects and the tool package test still passes.
- [ ] 3.2 Add a packaged-analyzer test: package contents, a consumer build through central package management from an isolated source, suggestions that do not warn, a silent test project, and the opt-in; verify it passes without network access.
- [ ] 3.3 Pack and publish both packages in the release workflow, and attach both to the GitHub release; verify the workflow's commands locally up to the push.
- [ ] 3.4 Build the consumer project with the locally installed .NET 8 SDK; verify the analyzer loads without CS8032 and reports the finding.

## 4. Consistency checks

- [ ] 4.1 Add consistency tests: the analyzer assembly references Roslyn 4.8 or lower, every analyzer source checks the scope and creates diagnostics through `EfDiagnostic`, and the README and usage guide document the package; verify each fails when its condition is broken.

## 5. Build-time cost

- [ ] 5.1 Measure analyzer time on the validation corpus with `-p:ReportAnalyzer=true`, and record the result in `validation/FINDINGS.md`; verify no rule dominates the compiler's analyzer time.

## 6. Documentation and verification

- [ ] 6.1 Add the README "CLI or analyzer package" section, the usage-guide section (install, central package management, default severities, `.editorconfig`, test projects, supported SDKs), and update the suppression guide, development guide, roadmap, contributing guide, and changelog.
- [ ] 6.2 Build `EFDoctor.sln` and run the full suite; verify zero warnings and all tests pass.
- [ ] 6.3 Run `openspec validate add-analyzer-nuget-package --strict`; verify the change is valid.
