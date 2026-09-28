## ADDED Requirements

### Requirement: Export the public repository as a scrubbed snapshot
The repository SHALL provide `scripts/export-public.sh`, which builds the public repository from the private `HEAD` without its history. The script SHALL:
- export only tracked files;
- remove every path listed in `scripts/export-exclude.txt`;
- require a denylist file that is not tracked, and fail when any denylisted term appears, case-insensitively, in an exported text file;
- fail when a kept text file mentions an excluded path, except in the archived changes (`openspec/changes/archive/`) and `scripts/`, and except for paths the exclusion list marks `generated:` because local tooling recreates them;
- set the package repository URL when `--repo owner/name` is given;
- create a new repository with a single initial commit authored by the identity passed to it, or stage the tree in an existing clone given by `--into`.

The script SHALL NOT push, and SHALL refuse to run with uncommitted changes.

#### Scenario: Clean export
- **WHEN** the maintainer runs the script with a denylist whose terms don't appear in the kept files
- **THEN** it creates a repository with one commit that contains the tracked files minus the excluded paths

#### Scenario: Denylisted term present
- **WHEN** a denylisted term appears in a file that would be exported
- **THEN** the script fails, names the file and the term, and creates no commit

#### Scenario: Dangling link to an excluded file
- **WHEN** a kept file mentions a path that the export excludes
- **THEN** the script fails and names the file and the path

#### Scenario: Missing denylist
- **WHEN** no denylist file exists at the default or given location
- **THEN** the script fails before exporting anything

#### Scenario: Verified export
- **WHEN** the script runs with `--verify`
- **THEN** it builds the exported tree and runs its full test suite, and fails if either fails

### Requirement: Keep private material out of the public tree
The public tree SHALL NOT contain the maintainer's agent instructions (`CLAUDE.md`), the private product brief, or the raw validation results. The public roadmap SHALL be `docs/roadmap.md`, and no kept file SHALL link to an excluded file.

#### Scenario: Roadmap replaces the product brief
- **WHEN** a reader follows the README's link to candidate rules and priorities
- **THEN** it leads to `docs/roadmap.md`

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
