# Proposal

## Why

EFDoctor is public at `github.com/vuglll/EFDoctor`, but it can only be installed by packing it from source. The `EFDoctor` package ID on nuget.org is already ours, from `0.1.0-preview.1` and `0.1.0-preview.2`. Those were pushed by hand, and they predate EFD009, EFD010, EFD014, and EFD020–EFD025, as well as every fix since.

The next step on the way to 1.0 (see `docs/roadmap.md`) is getting outside users. That needs a normal `dotnet tool install` and a release process that can be repeated safely.

## What Changes

- **Release workflow:** `.github/workflows/release.yml` runs when a `vX.Y.Z` tag is pushed. It:
  - checks that the tagged commit is on `main`, and that the tag matches the project's `<Version>`;
  - restores, builds, and runs the full test suite in Release;
  - packs the tool and pushes the package and its symbols to nuget.org, skipping a version that is already there;
  - creates a GitHub release with the tag's CHANGELOG section as notes and the package attached.
- **Trusted Publishing:** the workflow authenticates to nuget.org with Trusted Publishing, so no API key is stored in the repository. It runs in a `nuget` environment that only `v*` tags can deploy from.
- **Version 0.2.0:** the version moves from `0.1.0-preview.2` to `0.2.0`, a stable 0.x release. It isn't `-preview`, because the tool is usable now; 0.x still says the contract isn't frozen.
  - The CHANGELOG's Unreleased section becomes `## 0.2.0`.
  - The package release notes are rewritten.
  - The analyzer rules first released here move from `AnalyzerReleases.Unshipped.md` to `AnalyzerReleases.Shipped.md` under `## Release 0.2.0`.
- **Docs:** the README and `PACKAGE.md` install from nuget.org (`dotnet tool install --global EFDoctor`), and the README keeps packing from source as a separate section. `CONTRIBUTING.md` describes how to cut a release.

## Capabilities

### Modified Capabilities

- `public-release`: adds publishing tagged releases to nuget.org.
- `cli-analysis`: "Document tool installation" covers installing from nuget.org, as well as building and installing from source.

## Impact

- **New file:** `.github/workflows/release.yml`.
- **Changed files:** `EFDoctor.Cli.csproj` (version and release notes), the CHANGELOG, `AnalyzerReleases.*.md`, the README, `PACKAGE.md`, and `CONTRIBUTING.md`.
- **One-time setup outside the repository:**
  - a Trusted Publishing policy on nuget.org for `vuglll/EFDoctor` and `release.yml`, which the owner must add;
  - the `nuget` GitHub environment and the `NUGET_USER` repository variable, which can be set with `gh`.
- **Verification:** the full suite runs, and a local Release pack produces `EFDoctor.0.2.0.nupkg` with the expected metadata. The workflow itself runs for the first time when `v0.2.0` is tagged.
