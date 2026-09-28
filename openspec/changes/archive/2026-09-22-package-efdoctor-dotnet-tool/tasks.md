# Tasks

## 1. CLI behavior

- [x] 1.1 Add `--version` and `--help`/`-h` handling in `CliOptions`/`CliApplication` (informational version without build metadata; usage with options and exit codes; exit code `0`; no MSBuild registration); verify new `CliApplicationTests` cover version, both help spellings, and unchanged no-argument and unknown-command errors.
- [x] 1.2 Replace `RegisterDefaults` with target-relative SDK resolution in `MsBuildRegistration` (resolve from the target's directory, register once, descriptive error when none), remove the early registration from `Program.cs`, and report failures as analysis failures; verify tests for resolution without `global.json`, with a `global.json` pinning an installed SDK, and with a pin to a missing SDK (exit code `2` with a clear error).
- [x] 1.3 Probe whether Roslyn's BuildHost honors the selected SDK, using a temporary project whose `global.json` pins an older installed SDK and exposes `NETCoreSdkVersion` to the compilation; verify the observed SDK, and pause to revise the spec if the host selection is not honored.

## 2. Package definition

- [x] 2.1 Add tool and package metadata to `EFDoctor.Cli.csproj` (`PackAsTool`, `ToolCommandName`, `PackageId`, `Version`, `Description`, `PackageTags`, `PackageReadmeFile`, `PackageIcon`, `PackageReleaseNotes`, `RollForward`, snupkg symbols, `SatelliteResourceLanguages`, `PublishRepositoryUrl=false`, `EnableSourceLink=false`); verify `dotnet pack` succeeds with no warnings.
- [x] 2.2 Add `src/EFDoctor.Cli/Package/PACKAGE.md` and a generated 128×128 `icon.png`, and pack the root `NOTICE`; verify they appear at the expected package paths.
- [x] 2.3 Generate `THIRD-PARTY-NOTICES.md` from the CLI's dependency closure, with licenses verified from each package's `.nuspec`, and pack it; verify every bundled third-party assembly is listed and no non-permissive license is present.
- [x] 2.4 Move all shipped rules to `AnalyzerReleases.Shipped.md` under "Release 0.1.0", leave `AnalyzerReleases.Unshipped.md` with only its header, and add `CHANGELOG.md`; verify the analyzers build with release tracking enabled and no warnings.

## 3. Packaging verification

- [x] 3.1 Add a packaging smoke test that packs the built CLI, inspects the `.nupkg` (license expression, no repository URL, `NOTICE`, third-party notices, readme, icon, `BuildHost-netcore`, no MSBuild runtime assemblies), installs it into a temporary tool path from an isolated local source, and runs `--version` and `analyze` on `EFD001.Sample`; verify the test passes and the installed tool's JSON findings and exit code match the repository-built CLI.
- [x] 3.2 Pack and install the tool globally from `./artifacts` on this machine, then run it from an unrelated directory against a fixture and against `~/repos/JobSearch/JobSearch.sln`; verify the version output, findings, and exit codes, and record the package sizes.

## 4. Documentation and verification

- [x] 4.1 Add a README section on packing, installing (global and local manifest), running, updating, and uninstalling the tool, with the runtime, SDK, restore, and trust prerequisites; update the "not yet packaged" note and the release tracking references; verify the commands match what was run in 3.2.
- [x] 4.2 Build `EFDoctor.sln` and run the full suite with `--no-restore`; verify zero warnings or errors and all tests pass.
- [x] 4.3 Run `openspec validate package-efdoctor-dotnet-tool --strict`; verify the change is coherent and valid.
