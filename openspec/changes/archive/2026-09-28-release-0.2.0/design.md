# Design

## Decisions

### Tag-triggered, from main only

**Decision:** The workflow runs on `push` of tags matching `v*`. Its first step fails unless the tagged commit is an ancestor of `origin/main`, and unless the tag without its `v` equals the `<Version>` that MSBuild evaluates for `EFDoctor.Cli.csproj`.

**Why:**
- Releases are cut from `main`, per the branch model.
- A tag on a `dev` commit, or a tag that disagrees with the project version, would publish something that isn't what the tag says.

### Trusted Publishing instead of an API key

**Decision:**
- The job has `id-token: write`, and uses `NuGet/login@v1` with `user: ${{ vars.NUGET_USER }}` to exchange GitHub's OIDC token for a short-lived nuget.org key.
- The nuget.org policy is scoped to the owner `vuglll`, the repository `EFDoctor`, the workflow `release.yml`, and the environment `nuget`.

**Why:** There's no long-lived secret to leak or rotate. The key is valid for about an hour, and only for this workflow.

### A `nuget` environment limited to tags

**Decision:** The job runs in the `nuget` environment, whose deployment policy allows only tags matching `v*`.

**Why:** It is a second fence, beyond the tag trigger, and it is the environment name the nuget.org policy names.

### Test before publishing; tolerate re-runs

**Decision:**
- Restore, build, and test run in Release before `dotnet pack --no-build`.
- `dotnet nuget push artifacts/*.nupkg --skip-duplicate` pushes the package. The `.snupkg` beside it goes to the symbol server automatically.
- The GitHub release is created with `gh release create`, and skipped if the release already exists.

**Why:** A re-run after a partial failure, for example if the GitHub release step failed, must not fail on the already-published package.

### Release notes come from the CHANGELOG

**Decision:** An `awk` step extracts the lines between `## <version>` and the next `## ` heading. The release fails if that section is empty.

**Why:** The CHANGELOG stays the single place where release notes are written.

### 0.2.0, not 0.2.0-preview

**Decision:** Ship `0.2.0` without a prerelease suffix.

**Why:**
- On nuget.org, a prerelease version is hidden unless you ask for prereleases, and installing it needs `--prerelease`, which is friction for the first outside users.
- A 0.x major version already communicates that the contract may still change; the 1.0 checklist in the roadmap says when it won't.
