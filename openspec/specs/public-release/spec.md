# public-release Specification

## Purpose
Defines what the EFDoctor repository keeps out of version control, its continuous integration, its contributor and security documents, and how releases are published to nuget.org.

## Requirements

### Requirement: Keep private material out of the public tree
The repository SHALL git-ignore `validation/results/`, so that running the validation harness never stages raw reports or triage files, which quote code from the corpus projects. The public roadmap SHALL be `docs/roadmap.md`.

#### Scenario: Roadmap replaces the product brief
- **WHEN** a reader follows the README's link to candidate rules and priorities
- **THEN** it leads to `docs/roadmap.md`

#### Scenario: Validation run leaves the tree clean
- **WHEN** a contributor runs `validation/run_corpus.py`
- **THEN** `git status` shows no files under `validation/results/`

### Requirement: Run the test suite in continuous integration
The repository SHALL include a GitHub Actions workflow that restores, builds, and runs the full test suite with the .NET 10 SDK on every push and pull request.

#### Scenario: Pull request
- **WHEN** a pull request is opened or updated
- **THEN** the workflow builds the solution with warnings as errors and runs every test project

### Requirement: Document contribution and security reporting
The repository SHALL include `CONTRIBUTING.md`, which describes the OpenSpec workflow and the checklist for adding or changing a rule, and `SECURITY.md`, which describes how to report a vulnerability privately and the MSBuild trust boundary.

#### Scenario: New contributor adds a rule
- **WHEN** a contributor follows `CONTRIBUTING.md` to add a rule
- **THEN** it tells them which files and documents to update, and that `RepositoryConsistencyTests` checks them

### Requirement: Publish tagged releases to nuget.org
The repository SHALL include a GitHub Actions workflow that publishes a release when a tag `vX.Y.Z` is pushed. The tool package `EFDoctor` and the analyzer package `EFDoctor.Analyzers` SHALL take their version from one shared `<Version>` property. The workflow SHALL fail without publishing when:
- the tagged commit is not on `main`;
- the tag's version does not equal that `<Version>`;
- the build or any test fails;
- the CHANGELOG has no section for the version.

It SHALL authenticate to nuget.org with Trusted Publishing and SHALL NOT use a stored API key. It SHALL push the tool package with its symbols package, and the analyzer package, treating a version that is already published as success. It SHALL create a GitHub release for the tag, whose notes are the version's CHANGELOG section and which has both packages attached.

#### Scenario: Release from main
- **WHEN** a `v0.4.0` tag is pushed on a commit of `main` whose version is `0.4.0` and whose tests pass
- **THEN** `EFDoctor` 0.4.0 with its symbols, and `EFDoctor.Analyzers` 0.4.0, are published to nuget.org, and a GitHub release `v0.4.0` is created with the CHANGELOG notes and both packages

#### Scenario: Tag off main
- **WHEN** a version tag is pushed on a commit that is not on `main`
- **THEN** the workflow fails before building, and nothing is published

#### Scenario: Tag and version disagree
- **WHEN** the tag's version differs from the shared `<Version>`
- **THEN** the workflow fails before building, and nothing is published

#### Scenario: Re-run after a partial failure
- **WHEN** the workflow is re-run for a version that is already on nuget.org
- **THEN** the package push succeeds without re-publishing, and the remaining steps run
