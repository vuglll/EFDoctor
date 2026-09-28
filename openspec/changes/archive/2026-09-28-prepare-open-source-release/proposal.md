# Proposal

## Why

EFDoctor is ready to be published as an open-source repository. The private repository isn't fit to publish as it is:

- **History:** most commits are authored with a work email address, and two commit messages name a private project.
- **Private project name:** two archived changes name the private project that their validation ran against, and one quotes its class names.
- **Business plan:** `docs/product-brief.md` mixes the rule backlog with a private business plan (pricing, distribution, personal framing).
- **Internal files:** `CLAUDE.md` holds maintainer and agent instructions, and `validation/results/` holds raw reports and triage files that quote code lines from the corpus projects.
- **Missing pieces:** there is no CI, no contributor guide, and no security policy. The package deliberately carries no repository URL ("while the source repository is private").

## What Changes

- **Public snapshot:** `scripts/export-public.sh` builds the public repository as a fresh snapshot of `HEAD`, never the private history:
  - It exports only tracked files and removes the paths listed in `scripts/export-exclude.txt`.
  - It fails if any term from a local, untracked denylist file appears in the exported tree, or if a kept file links to an excluded path.
  - It sets the repository URL when `--repo owner/name` is given.
  - It creates one initial commit under an identity given on the command line, and optionally builds and tests the exported tree.
  - It never pushes.
- **Excluded from the public tree:** `CLAUDE.md`, `docs/product-brief.md`, and `validation/results/`.
- **Kept:** the OpenSpec specs, archived changes, and OpenSpec tool files. `validation/FINDINGS.md` and `SUMMARY.md` also stay, because the archived changes cite them as evidence.
- **Roadmap:** `docs/roadmap.md` is the public roadmap. It holds the candidate-rule backlog, priorities, the rule contract and quality bar, cross-cutting work, and what is out of scope. It becomes the single source for the backlog: the product brief's catalog is replaced by a pointer, and README and `CLAUDE.md` links move to the roadmap.
- **Scrub:** the private project's name and class names are removed from the archived changes that mention them.
- **Contributor documents:** `CONTRIBUTING.md` carries the contributor-relevant guidance from `CLAUDE.md`: the OpenSpec workflow, the rule checklist, the fixture/solution quirk, spec tracing, and descriptor consistency. `SECURITY.md` explains how to report a vulnerability and restates the MSBuild trust boundary.
- **CI:** `.github/workflows/ci.yml` restores, builds, and runs the full test suite on Ubuntu for pushes and pull requests.
- **Package URL:** the package carries repository and project URLs, with Source Link, only when the `EFDoctorRepositoryUrl` property is set. It is unset by default, which keeps today's behavior. The export script sets it.
- **README:** it becomes public-facing. The commercial wording in the principles and out-of-scope list is replaced, and the links point to the roadmap.
- **Validation:** `validation/README.md` says that `results/` is a local output, which the public repository doesn't track.

## Capabilities

### New Capabilities

- `public-release`: how the public repository is produced from the private one, and the CI and contributor documents it ships with.

### Modified Capabilities

- `cli-analysis`: "Distribute the CLI as a .NET tool package" embeds repository URLs only when a repository URL is configured.

## Impact

- **New files:**
  - `scripts/export-public.sh` and `scripts/export-exclude.txt`;
  - `docs/roadmap.md`;
  - `CONTRIBUTING.md` and `SECURITY.md`;
  - `.github/workflows/ci.yml`.
- **Changed files:** `Directory.Build.props`, `EFDoctor.Cli.csproj`, the README, `CLAUDE.md`, `docs/product-brief.md`, `validation/README.md`, one archived change, and the CHANGELOG.
- **Tests:** the existing package test still asserts that there is no URL by default. The export is verified by running it with `--verify`, which builds and runs the full suite inside the exported tree.
