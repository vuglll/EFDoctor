# Tasks

## 1. Roadmap and documentation

- [x] 1.1 Write `docs/roadmap.md` from the public parts of the product brief. Replace the brief's catalog with a pointer, and update the README and `CLAUDE.md` links.
- [x] 1.2 Make the README public-facing: principles, out-of-scope list, repository layout, package-URL wording, and a `<!-- ci-badge -->` marker.
- [x] 1.3 Scrub the private project's name and class names from the archived changes.
- [x] 1.4 Add `CONTRIBUTING.md` and `SECURITY.md`. Describe the validation outputs in `validation/README.md` as local.

## 2. Package URL

- [x] 2.1 Add `EFDoctorRepositoryUrl` to `Directory.Build.props`, and the conditional URL and Source Link properties to `EFDoctor.Cli.csproj`. Keep the default package test asserting that there is no URL.

## 3. CI

- [x] 3.1 Add `.github/workflows/ci.yml`: Ubuntu, .NET 10, restore/build/test, with NuGet cache.

## 4. Export

- [x] 4.1 Add `scripts/export-exclude.txt` and `scripts/export-public.sh`, with the denylist, dangling-link check, `--repo`, `--into`, `--author`, and `--verify`.
- [x] 4.2 Run the export with `--verify` against a local denylist, confirm the checks fail on a planted term and a planted link, and inspect the exported tree.

## 5. Verification

- [x] 5.1 Update the CHANGELOG (Unreleased), build with no warnings, and run the full suite.
- [x] 5.2 Run `openspec validate prepare-open-source-release --strict`.
