## REMOVED Requirements

### Requirement: Export the public repository as a scrubbed snapshot
**Reason**: This repository is now the only source of truth. Contributions are merged here, and an export from another repository would overwrite them.
**Migration**: None. Work happens directly in this repository through pull requests into `dev`.

## MODIFIED Requirements

### Requirement: Keep private material out of the public tree
The repository SHALL git-ignore `validation/results/`, so that running the validation harness never stages raw reports or triage files, which quote code from the corpus projects. The public roadmap SHALL be `docs/roadmap.md`.

#### Scenario: Roadmap replaces the product brief
- **WHEN** a reader follows the README's link to candidate rules and priorities
- **THEN** it leads to `docs/roadmap.md`

#### Scenario: Validation run leaves the tree clean
- **WHEN** a contributor runs `validation/run_corpus.py`
- **THEN** `git status` shows no files under `validation/results/`
