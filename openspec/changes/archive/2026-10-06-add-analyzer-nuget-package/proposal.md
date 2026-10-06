# Proposal

## Why

EFDoctor's rules reach a team only when someone runs the `efdoctor` tool. Teams asked for the rules as a Roslyn analyzer package, like Meziantou.Analyzer or SonarAnalyzer.CSharp, so that one reference in central package management runs them on every project, in every build and in the IDE (issue #12). The rules are already `DiagnosticAnalyzer`s in a `netstandard2.0` assembly with no dependency beyond Roslyn, so this is mostly packaging, plus the defaults that make sense inside a build.

## What Changes

- Publish a second NuGet package, `EFDoctor.Analyzers`, with the analyzer assembly under `analyzers/dotnet/cs`. It is a development dependency with no dependencies of its own, and it has the same version as the `EFDoctor` tool.
- Compile the analyzers against Roslyn 4.8, the .NET 8 SDK baseline, so that older SDKs and Visual Studio 17.8 load them instead of dropping them with CS8032. The CLI keeps Roslyn 5.9.0.
- Give findings a build severity that follows their confidence: high-confidence findings keep the rule's severity (a warning, or a suggestion for EFD025), and medium-confidence and advisory findings are suggestions. A rule that never reports above medium confidence gets `Info` as its default severity. `.editorconfig` overrides work as for any analyzer.
- Keep CLI report severities as they are: a medium-confidence finding is still a `Warning` in console and JSON output.
- Skip test projects in the analyzers themselves, using the CLI's classification (the `IsTestProject` property, or a test-framework reference). The package makes `IsTestProject` visible to the compiler, and `EFDoctorAnalyzeTestProjects=true` opts a project back in.
- Set a help link on every rule, pointing to its page in `docs/rules/`.
- Pack both packages in the release workflow and publish them with one version, which moves to `Directory.Build.props`.
- Document the choice between the CLI and the analyzer package, the default severities, and the `.editorconfig` overrides.

Code fixes are a follow-up and not part of this change.

## Capabilities

### New Capabilities

- `analyzer-package`: the `EFDoctor.Analyzers` package, the compiler versions it loads in, build severities, test-project handling, help links, and its documentation.

### Modified Capabilities

- `finding-reporting`: report severity stays derived from confidence, independent of the build severity of the diagnostic.
- `public-release`: a release publishes the tool package and the analyzer package with the same version.

## Impact

Affects every analyzer (a shared scope check, a shared diagnostic factory, and a help link), `EFDoctor.Analyzers.csproj` and its new package assets, `Directory.Build.props`, the CLI's diagnostic mapping and test-project classification, the release workflow, the analyzer release-tracking files, the tests (analyzer harness, consistency checks, and a packaged-analyzer test), and the README, usage, suppression, development, roadmap, and contributing documents. Detection logic, the finding contract, the JSON schema, and exit codes do not change.
