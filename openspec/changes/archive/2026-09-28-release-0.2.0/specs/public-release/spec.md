## ADDED Requirements

### Requirement: Publish tagged releases to nuget.org
The repository SHALL include a GitHub Actions workflow that publishes a release when a tag `vX.Y.Z` is pushed. The workflow SHALL fail without publishing when:
- the tagged commit is not on `main`;
- the tag's version does not equal the tool project's `<Version>`;
- the build or any test fails;
- the CHANGELOG has no section for the version.

It SHALL authenticate to nuget.org with Trusted Publishing and SHALL NOT use a stored API key. It SHALL push the tool package and its symbols package, treating a version that is already published as success. It SHALL create a GitHub release for the tag, whose notes are the version's CHANGELOG section and which has the package attached.

#### Scenario: Release from main
- **WHEN** a `v0.2.0` tag is pushed on a commit of `main` whose tool version is `0.2.0` and whose tests pass
- **THEN** `EFDoctor` 0.2.0 and its symbols are published to nuget.org, and a GitHub release `v0.2.0` is created with the CHANGELOG notes

#### Scenario: Tag off main
- **WHEN** a version tag is pushed on a commit that is not on `main`
- **THEN** the workflow fails before building, and nothing is published

#### Scenario: Tag and version disagree
- **WHEN** the tag's version differs from the tool project's `<Version>`
- **THEN** the workflow fails before building, and nothing is published

#### Scenario: Re-run after a partial failure
- **WHEN** the workflow is re-run for a version that is already on nuget.org
- **THEN** the package push succeeds without re-publishing, and the remaining steps run
