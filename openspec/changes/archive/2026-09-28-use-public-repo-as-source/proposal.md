# Proposal

## Why

The public repository started as a scrubbed export of a private one, made by `scripts/export-public.sh`. Contributions now arrive as pull requests to the public repository. With two sources of truth, the next export would silently undo any contribution merged here. So this repository becomes the only source of truth, and the export tooling goes away.

## What Changes

- Remove `scripts/export-public.sh` and `scripts/export-exclude.txt`.
- Git-ignore `validation/results/`. Running the validation harness then never stages raw reports or the corpus projects' code lines.
- Update the README layout, the `Directory.Build.props` comment, a test comment, and `validation/README.md`, all of which describe the export.
- **`public-release` spec:**
  - The export requirement is removed.
  - "Keep private material out of the public tree" becomes a requirement to keep generated validation output out of the repository.
  - The Purpose no longer describes a private source.

## Capabilities

### Modified Capabilities

- `public-release`: removes the export requirement, and replaces the private-material requirement with one for untracked validation output.

## Impact

- **Files:** the two scripts are deleted. `.gitignore`, the README, `Directory.Build.props`, `PackagedToolTests.cs`, and `validation/README.md` change.
- **Behavior:** none for users. The package still takes its repository URL from `EFDoctorRepositoryUrl`, which is now set directly in `Directory.Build.props`.
