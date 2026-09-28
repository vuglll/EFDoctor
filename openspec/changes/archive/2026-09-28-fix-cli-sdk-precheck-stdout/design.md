# Design

## Context

`MsBuildRegistration.TryResolveSdk` calls `MSBuildLocator.QueryVisualStudioInstances` with the target directory as `WorkingDirectory`. When `global.json` can't be satisfied, hostfxr throws, and we turn the message into a clear error. The existing in-process tests pass, because they capture `TextWriter`s and never see what native code writes to file descriptor 1.

## Decisions

### Pre-check in a child process rather than redirect native stdout

**Decision:** Run `<dotnet> --version` with `WorkingDirectory = targetDirectory`, `RedirectStandardOutput` and `RedirectStandardError`, and a 30-second timeout. Treat a non-zero exit whose stderr contains `A compatible .NET SDK was not found` as a resolution failure. Pass the combined captured text to `DescribeSdkResolutionFailure`, which already parses "Requested SDK version" and "global.json file".

**Why:**
- It is exact: the muxer and MSBuildLocator both resolve through hostfxr, so they apply the same `global.json` roll-forward rules. We don't reimplement roll-forward.
- It is portable. Redirecting descriptor 1 around the query would need `dup`/`dup2` P/Invoke on Unix and CRT `_dup2` plus `SetStdHandle` on Windows, and would briefly hide any other output.

**Alternatives:**
- Parse `global.json` and compare it with `--list-sdks`. Rejected: this reimplements roll-forward policy and would diverge from hostfxr.
- Redirect native stdout. Rejected for the reasons above.

### Run only when a global.json applies

**Decision:** Walk up from the target directory looking for `global.json`. Without one, hostfxr picks the newest SDK and can't fail this way, so the CLI skips the pre-check.

**Why:** Most targets pay nothing, and targets with a `global.json` pay one short process start, which is well under a design-time build.

### Fail open

**Decision:** If the host can't be located or started, times out, or fails for another reason, the pre-check returns "no verdict" and registration proceeds as today. The in-process check and its error message remain the fallback.

**Why:** The pre-check exists only to keep stdout clean. It must never turn a working analysis into a failure.

### Locating dotnet

**Decision:** Try, in order:
1. `DOTNET_HOST_PATH`, if the file exists;
2. `Environment.ProcessPath`, when its file name is `dotnet` or `dotnet.exe`, which is the case under `dotnet efdoctor.dll`;
3. `DOTNET_ROOT/dotnet[.exe]`;
4. `dotnet` on `PATH`.

**Why:** This matches how MSBuildLocator finds the host, so both checks see the same SDK set.
