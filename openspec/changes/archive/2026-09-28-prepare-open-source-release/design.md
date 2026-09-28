# Design

## Decisions

### A fresh snapshot, not a history rewrite

**Decision:** The public repository starts from a single commit made from an exported snapshot of the private `HEAD`. Later releases repeat the export into the public clone (see "Updating the public repository" below).

**Why:**
- It removes the work-email authorship and private names in commit messages without the risk of a history rewrite.
- The private repository stays the source of truth.
- The archived OpenSpec changes still document how each rule was built, so the delivery story is not lost.

### Exclusions live in a tracked file; the denylist does not

**Decision:**
- `scripts/export-exclude.txt` lists the paths to drop, one per line, relative to the repository root. It is harmless to publish.
- The terms that must never appear (private project names, work domains, local paths) live in a denylist file that is never committed. Its default location is `~/.config/efdoctor/export-denylist`, overridable with `--denylist`. The script refuses to run without one.
- Matching is case-insensitive over every exported text file.

**Why:** A tracked denylist would publish the very terms it guards.

### No dangling links to excluded files

**Decision:** After removing the excluded paths, the script searches the kept text files for each excluded path and fails on a hit. Paths marked `generated:` in the exclusion list, such as `validation/results`, are exempt, because the tooling recreates them and names them by design. The check also skips `openspec/changes/archive/`, which records the repository as it was when each change was made, and `scripts/`, which names the excluded paths by design.

**Why:** The README once linked `docs/product-brief.md`. A published link to a file that isn't there looks broken, and it reveals that files were removed.

### The repository URL is one property

**Decision:**
- `Directory.Build.props` defines `EFDoctorRepositoryUrl`, which is empty by default.
- When it is set, `EFDoctor.Cli.csproj` sets `RepositoryUrl`, `PackageProjectUrl`, `RepositoryType=git`, `PublishRepositoryUrl=true`, and `EnableSourceLink=true`. Otherwise it keeps them off.
- `--repo owner/name` writes `https://github.com/owner/name` into the exported `Directory.Build.props`, and adds a CI badge where the README has a `<!-- ci-badge -->` marker.

**Why:** One edit, in a file the script owns, rather than rewriting prose. The private build keeps today's no-URL package.

### CI on Ubuntu only, for now

**Decision:** One job on `ubuntu-latest`:
- `actions/setup-dotnet` installs 10.0.x;
- restore, build, and test the solution;
- cache NuGet packages.

There is no Windows or macOS job.

**Why:**
- The suite was developed on macOS. Ubuntu is close to it.
- Windows path handling in the end-to-end tests has never been exercised, so adding it now would publish a red badge on day one. A Windows job is a follow-up.
- The multi-framework workspace fixture needs the `net8.0` reference pack, which restore downloads.

### Contributor documents

**Decision:**
- `CONTRIBUTING.md` holds the rule-delivery workflow and checklists. It is adapted from `CLAUDE.md`, which is agent-oriented and stays private.
- `SECURITY.md` asks for private vulnerability reports through GitHub's advisory form, and repeats that analyzing a repository runs its MSBuild logic.
- No code of conduct or issue templates are added yet.

**Why:** Keep only what a contributor needs on day one.

### Updating the public repository

**Decision:** The script can also export into an existing clone of the public repository (`--into <dir>`). It then replaces the working tree and stages the result. Committing and pushing are left to the maintainer.
