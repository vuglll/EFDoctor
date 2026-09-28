# Contributing to EFDoctor

Thanks for helping. EFDoctor's value is precision: a finding should be right, explain itself, and name the cases where it doesn't apply. The quality bar in the [roadmap](docs/roadmap.md#the-quality-bar) applies to every rule.

## Setup

You need the .NET 10 SDK. For spec work, you also need the [OpenSpec](https://github.com/Fission-AI/OpenSpec) CLI.

```bash
dotnet restore EFDoctor.sln
dotnet build EFDoctor.sln --no-restore
dotnet test EFDoctor.sln --no-restore

npm install -g @fission-ai/openspec@latest
openspec list --specs
```

The build treats warnings as errors. Run the full suite before opening a pull request; continuous integration runs the same commands.

## How changes are made

Every behavior change goes through OpenSpec:

1. **Propose.** Create `openspec/changes/<name>/` with a proposal, a design, spec deltas, and a task list. A new rule is named `add-efdNNN-…`, and a change to an existing rule `improve-…` or `fix-…`.
2. **Discuss.** Open a draft pull request with just the change artifacts, so the behavior can be agreed before the code is written. This also shows others that the rule is taken.
3. **Implement** against the task list.
4. **Archive.** When the work is done, run `openspec archive <name>`. That syncs the deltas into `openspec/specs/` and moves the change to `openspec/changes/archive/`.

Before picking a rule, check `openspec/changes/` and the open pull requests, so you don't duplicate work in progress.

### Branches

- **`dev`** is the default branch. Open pull requests against `dev`.
- **`main`** holds releases only. A release is a pull request from `dev` to `main`, tagged `vX.Y.Z` after it merges.

Both branches require the CI check to pass, and an approving review from a code owner (listed in [`.github/CODEOWNERS`](.github/CODEOWNERS)), before a pull request can merge. GitHub doesn't count an author's approval of their own pull request.

### Cutting a release

1. On `dev`, set `<Version>` in `src/EFDoctor.Cli/EFDoctor.Cli.csproj` and update `<PackageReleaseNotes>`.
2. In `CHANGELOG.md`, turn the **Unreleased** section into `## X.Y.Z`, and start a new, empty **Unreleased** section above it.
3. In `src/EFDoctor.Analyzers/`, move any rules from `AnalyzerReleases.Unshipped.md` to `AnalyzerReleases.Shipped.md` under `## Release X.Y.Z`.
4. Merge `dev` into `main` through a pull request, using **Create a merge commit**. Squashing would give `main` a commit that `dev` doesn't have, and later release pull requests would show old changes again. Pull requests into `dev` are squash-merged.
5. Tag the merge commit on `main` and push the tag:

   ```bash
   git tag vX.Y.Z && git push origin vX.Y.Z
   ```

The `Release` workflow then checks that the tag is on `main` and matches `<Version>`, runs the tests, publishes the package to nuget.org through Trusted Publishing, and creates the GitHub release from the CHANGELOG section.

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/): `feat(analyzers): …`, `fix(efd005): …`, `docs(openspec): …`.

## Adding or changing a rule

`tests/EFDoctor.Cli.Tests/RepositoryConsistencyTests.cs` runs with every `dotnet test`. It fails with a message that names exactly what is missing.

**Checked automatically.** Every shipped rule has all of these, and none may exist for a rule that isn't shipped:

- registration in `WorkspaceAnalyzer`
- a spec folder, `openspec/specs/efdNNN-…`
- a rule page, `docs/rules/EFDNNN.md`
- a row in the README rule table
- coverage in the README **Project status** line, and in the analyzer-test list of the **Verification map** in `docs/development.md` (a range such as "EFD011 through EFD014" counts)
- a row in the `src/EFDoctor.Cli/Package/PACKAGE.md` rule table
- an `AnalyzerReleases` entry
- a `CHANGELOG.md` entry, under **Unreleased** for a new rule

**Still manual.** Update these too; the checks don't parse them:

- `docs/development.md`: the **Implementation history** list, and the end-to-end list in the **Verification map**
- `docs/suppression.md`: the `SuppressMessage` category note when you add a category, and a `For EFDNNN, use diagnostic ID …` paragraph
- `docs/rules/README.md`: an `## EFDNNN … boundary` section

**Don't add a `cli-analysis` requirement for a new rule.** The requirement "Run every shipped analyzer during workspace analysis" covers every rule, and the registration check enforces it. A rule's behavior belongs in its own capability spec.

**Keep analyzer descriptions consistent with the spec.** A descriptor's `description:` text must not contradict the rule's spec.

### Trace scenarios to tests

For a new rule, or any rule whose spec you change, tag the tests that cover each scenario:

```csharp
[Trait("Spec", "efd023-leading-wildcard-search/Prefix search")]
```

Once any test tags a capability, every scenario in it must be tagged, and every tag must name a real scenario. `efd023-leading-wildcard-search` is the reference example.

### Fixtures

- Analyzer tests compile small snippets. Each rule needs at least ten positive and ten negative cases, including the legitimate shapes that must not be reported.
- End-to-end tests run the CLI against buildable sample projects in `tests/Fixtures/`.

**Adding a fixture to the solution.** `dotnet sln add … --solution-folder Fixtures` creates a second "Fixtures" solution folder instead of reusing the existing one (`{F12727AA-ABEF-07AF-E6F0-CD96E3D9A8AF}`). After adding a fixture, delete the new folder's `Project(...)`/`EndProject` entry, and nest the fixture under `{F12727AA-…}`. `RepositoryConsistencyTests` fails if the solution has more than one "Fixtures" folder, or if a fixture project is missing from it. A fixture that sets `IsTestProject` is the exception: it stays out of the solution.

If a rule adds a case to the shared test-project fixture (`tests/Fixtures/EFD005.TestSample`), update the expected count and rule order in `TestProjectFindingsFromMultipleRulesAreDowngradedButRemainReported`.

## Validating against real projects

Precision is measured on real EF Core projects. [`validation/README.md`](validation/README.md) explains the corpus, the harness, and how to triage findings. A rule change that could add or remove findings should be re-run against the corpus, and every new finding triaged.

## Reporting false positives

A false positive is the most useful bug report we can get. Please include:

- the smallest code that reproduces it;
- the finding's JSON (`--format json`);
- why the code is fine as written.

## Security

See [`SECURITY.md`](SECURITY.md). Please don't report vulnerabilities in public issues.

## License

By contributing, you agree that your contributions are licensed under the [Apache License 2.0](LICENSE).
