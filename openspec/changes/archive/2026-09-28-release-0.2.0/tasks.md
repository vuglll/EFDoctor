# Tasks

## 1. Release workflow

- [x] 1.1 Add `.github/workflows/release.yml`, with the tag/branch/version guards, a Release build and test, pack, `NuGet/login` Trusted Publishing, `nuget push --skip-duplicate`, and a GitHub release built from the CHANGELOG section.

## 2. Version and release notes

- [x] 2.1 Set `<Version>` to `0.2.0`, and rewrite `<PackageReleaseNotes>`.
- [x] 2.2 Turn the CHANGELOG's Unreleased section into `## 0.2.0`, and add an empty Unreleased section above it.
- [x] 2.3 Move the rules in `AnalyzerReleases.Unshipped.md` to `AnalyzerReleases.Shipped.md` under `## Release 0.2.0`.

## 3. Documentation

- [x] 3.1 README: install from nuget.org first, and keep a "Build and install from source" section.
- [x] 3.2 `PACKAGE.md`: drop `--prerelease` and the preview banner wording.
- [x] 3.3 `CONTRIBUTING.md`: how to cut a release.

## 4. Verification

- [x] 4.1 Build with no warnings, and run the full suite.
- [x] 4.2 Pack in Release and inspect the package's version, release notes, and URLs.
- [x] 4.3 Run `openspec validate release-0.2.0 --strict`.

## 5. Outside the repository

- [x] 5.1 Create the `nuget` environment, limited to `v*` tags, and the `NUGET_USER` variable on GitHub.
- [ ] 5.2 The owner adds the Trusted Publishing policy on nuget.org.
