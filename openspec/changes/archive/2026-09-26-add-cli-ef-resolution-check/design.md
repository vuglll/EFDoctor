# Design

## Context

`WorkspaceAnalyzer.AnalyzeAsync` opens the target with MSBuildWorkspace and then analyzes every C# project whose compilation has syntax trees. It skips test projects unless `--include-test-projects` is given. Workspace failure diagnostics go into one queue, and they are shown only when nothing at all is analyzable.

## Decisions

### Detect "uses EF Core" from source, not from references

**Decision:** A project uses EF Core when any of its syntax trees has a `using` directive, including a `global using`, whose name is `Microsoft.EntityFrameworkCore` or starts with `Microsoft.EntityFrameworkCore.`.

**Why:** We need a signal that works even when the compilation has no references. That is exactly the failure case: OpenIddict's compilations have no references, not even `System`. The source text is still there.

**Alternatives:**
- Parse the project file for `PackageReference`. Rejected: this misses transitive or central package references.
- Read the assets file. Rejected: OpenIddict *had* an assets file and still failed.

### Detect "unresolved" as a missing `DbContext`

**Decision:** Call `compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext")`. A `null` result means the project is unanalyzable.

**Why:** Every rule's compilation-start action already returns early in that case. This is precisely the condition under which EFDoctor is blind.

### Warnings on stderr; failure only when nothing EF was analyzable

**Decision:**
- `AnalysisResult` gains `Warnings`.
- `CliApplication` writes each warning to `errorOutput` as `EFDoctor warning: …`, in both formats, and even with `--quiet`, because the warnings are essential.
- When `efProjects > 0 && analyzableEfProjects == 0`, the result is `AnalysisResult.Failure` with a message that lists the projects and their causes.

**Why:**
- The JSON schema stays at version 1 with no new field, and stdout stays parseable.
- A solution where some EF projects are analyzable still produces useful, correct findings for those projects, so failing it would be a regression. The warning makes the partial coverage visible.

### Load diagnostics per project

**Decision:** The workspace failure handler already collects messages. A warning includes up to three collected messages that contain the project's file path or its directory, each truncated to 300 characters.

**Why:** OpenIddict's messages ("The TargetFramework value … was not recognized") are what tell the user what to fix.

## Risks / Trade-offs

- **[Risk]** A project that only mentions EF Core in a `using` it doesn't need gets flagged if EF is not referenced. → The compiler would already report that `using` as an error. Flagging it is correct.
- **[Risk]** Existing users with partially broken solutions now see warnings on stderr. → That is the intent. The exit code only changes when nothing EF was analyzable, which today is a false "clean" result.
