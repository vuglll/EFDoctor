# Design

## Context

`src/EFDoctor.Analyzers` targets `netstandard2.0`, references only `Microsoft.CodeAnalysis.CSharp` (5.9.0, private), and holds one `DiagnosticAnalyzer` per rule: 22 today. Every analyzer registers a compilation-start action and returns early when EF Core types are missing. Every diagnostic carries its confidence (`high`, `medium`, or `advisory`) and the other finding fields as diagnostic properties.

The CLI project references that assembly, runs the analyzers through `CompilationWithAnalyzers`, and maps each diagnostic to a finding. Report severity comes from the diagnostic's severity, except that advisory findings are always `Info`. The CLI classifies test projects itself (the `IsTestProject` property, or a reference to xunit, NUnit, the Visual Studio test platform, or `Microsoft.NET.Test.Sdk`), skips them by default, and downgrades their findings when `--include-test-projects` is set.

Descriptor severities today: `Info` for EFD023 and EFD025, `Warning` for everything else, including the rules that only ever report medium confidence (EFD006, EFD009, EFD010, EFD013, EFD020) and EFD005, whose findings are high, medium, or advisory. No descriptor has a help link.

The tool version is `<Version>` in `EFDoctor.Cli.csproj`, and the release workflow packs that one project.

## Goals / Non-Goals

**Goals:**

- One `PackageReference`, also through central package management, runs the rules in every build and in the IDE.
- Adopting the package cannot break a warnings-as-errors build on a judgment call.
- The CLI's output does not change.

**Non-Goals:**

- Code fixes.
- Multi-targeting several Roslyn versions.
- A second copy of the rules, or any difference in detection between the CLI and the package.

## Decisions

### Pack the existing analyzer project

`EFDoctor.Analyzers.csproj` becomes packable with `PackageId` `EFDoctor.Analyzers`: `IncludeBuildOutput=false`, the assembly packed to `analyzers/dotnet/cs/`, `DevelopmentDependency=true`, and `SuppressDependenciesWhenPacking=true`. The assembly embeds its PDB, because a symbols package has nothing to index when the package has no `lib` folder. Package assets live in `src/EFDoctor.Analyzers/Package/`: a short package readme and the build props file below. The icon, `NOTICE`, and `CHANGELOG.md` are shared with the tool. `THIRD-PARTY-NOTICES.md` is left out, because the package redistributes no third-party code.

A separate packaging project was considered and rejected: it would only wrap one assembly, and the CLI's project reference is unaffected by the analyzer project being packable.

### Compile against Roslyn 4.8

The analyzer project overrides the central Roslyn version with `VersionOverride="4.8.0"`. A probe build found one newer API: `ICollectionExpressionOperation` in EFD027. It is replaced by matching the operation kind's numeric value and reading the operation's child operations, which are its elements. Matching `CollectionExpressionSyntax` isn't enough: a `params` span argument, as in `Task.WhenAll(a, b)` on current frameworks, is an implicit collection expression with no such syntax. A test pins the numeric value to `OperationKind.CollectionExpression`. The CLI and the tests keep Roslyn 5.9.0, which loads an assembly compiled against 4.8.

A consistency test reads the built analyzer assembly's reference to `Microsoft.CodeAnalysis` and fails when it is newer than 4.8, so a later edit cannot raise the baseline unnoticed.

### Build severity follows confidence, through one diagnostic factory

A new internal `EfDiagnostic.Create` replaces every direct `Diagnostic.Create` call. It reads the confidence property and creates the diagnostic with an effective severity: the descriptor's default for high confidence, and at most `Info` for medium and advisory. An explicit effective severity is still overridden by `.editorconfig`, because the compiler applies configured severities per diagnostic ID after the analyzer reports.

The descriptors of the rules that never report above medium (EFD006, EFD009, EFD010, EFD013, EFD020) change to `Info`, so that the IDE and the release-tracking file show the real default. EFD005 stays `Warning` and relies on the factory for its medium and advisory findings. The changes are recorded under "Changed Rules" in `AnalyzerReleases.Unshipped.md`.

Alternatives considered:

- *A `.globalconfig` in the package that lowers severities per rule.* It cannot express EFD005's per-finding confidence, and the CLI would see different severities depending on whether the analyzed project references the package.
- *Leave descriptors at `Warning` and only cap at report time.* Tooling would show a default that no build ever produces.

### The CLI derives report severity from confidence

`DiagnosticFindingMapper` keeps its rules, with one addition: a medium-confidence diagnostic that arrives at `Info`, the capped build severity, maps to `Warning`. High-confidence findings still map from the diagnostic's severity, so EFD025 stays `Info`, and a severity that the analyzed project configures to `warning` or `error` is still honored. Advisory findings stay `Info`.

One edge changes: a medium-confidence rule that a project explicitly sets to `suggestion`, or that a warnings-as-errors project used to escalate to an error, now reports as `Warning` in the CLI. Neither behavior was specified, and both are indistinguishable from the default once severities are capped.

### Analyzers decide whether a compilation is in scope

A new `EfAnalysisScope.Includes(context)` is the first check in every analyzer's compilation-start action. It returns `false` for a test project unless the opt-in is set:

- A test project is one whose `build_property.IsTestProject` is `true`, or whose compilation references a recognized test framework. The reference check moves from the CLI into a shared `EfTestProjectDetection` in the analyzer assembly, and the CLI calls it, so the two classifications cannot drift.
- The opt-in is `build_property.EFDoctorAnalyzeTestProjects=true`.

MSBuild properties reach analyzers only when declared compiler-visible, so the package ships `build/EFDoctor.Analyzers.props` with `CompilerVisibleProperty` items for `IsTestProject` and `EFDoctorAnalyzeTestProjects`.

The CLI still skips test projects before running any analyzer. With `--include-test-projects`, it wraps the project's analyzer options so that the opt-in reads `true`, and then downgrades the findings as today.

A common base class for the analyzers was considered. The one-line check is smaller, and a consistency test over the analyzer sources fails when an analyzer lacks it or calls `Diagnostic.Create` directly.

### Help links point to the rule pages on `main`

Each descriptor gets `helpLinkUri: EfHelpLinks.For(DiagnosticId)`, which is `https://github.com/vuglll/EFDoctor/blob/main/docs/rules/EFDNNN.md`. `main` holds releases only, so the page matches the latest published package. A consistency test checks every descriptor, that the link starts with `EFDoctorRepositoryUrl` from `Directory.Build.props`, and that the page exists.

### One version for both packages

`<Version>` moves from `EFDoctor.Cli.csproj` to `Directory.Build.props`, so both packages always carry the same version. The release workflow still reads the version from the CLI project, packs both projects, and pushes every package in the output folder. The GitHub release attaches both.

### Verify the package with a real build

A new test packs the analyzer project, then builds a temporary consumer project that gets the package through central package management, from an isolated source: the packed folder plus the machine's global packages folder as a second local feed for EF Core, so the test makes no network call. It checks the package contents, that a high-confidence rule produces a build warning, that a medium-confidence rule does not, that a test project is silent, and that the opt-in brings findings back. The build writes a SARIF error log, which also shows suggestions and help links.

Loading on an actual .NET 8 SDK is checked by hand during implementation with the locally installed 8.0 SDK, because continuous integration installs only .NET 10.

### Build-time cost

The cost is measured once on the validation corpus with the compiler's analyzer report (`-p:ReportAnalyzer=true`), and the result is recorded in `validation/FINDINGS.md`. EFD003, the only rule that reads a whole model snapshot, already limits itself to the snapshot's `BuildModel` operation block, and returns at compilation start when the project has no snapshot type or eligible provider.

## Risks / Trade-offs

- [The first publish of a new package ID through Trusted Publishing] → The nuget.org policy is owner-scoped, so it should cover a new ID. If the first push is rejected, push `EFDoctor.Analyzers` once by hand, as was done for the tool's previews; the workflow treats an existing version as success.
- [A project that references a test framework without being a test project is skipped] → Same classification as the CLI, documented, with a one-property opt-in.
- [Collection expressions on Roslyn 4.8 itself] → The kind-based match is covered by the existing EFD027 tests on the current compiler. A compiler that predates the operation kind gives the worst case of a missed EFD027 finding for `Task.WhenAll([..])`, not a false one.
- [Suggestions are invisible in command-line build output] → Intended: they show in the IDE, and the CLI still reports them. The documentation shows how to raise a rule to `warning`.

## Migration Plan

Nothing changes for CLI users. The analyzer package first ships with the next release. Rollback is removing the package reference.
