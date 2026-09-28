# Proposal

## Why

When no installed SDK satisfies a target's `global.json`, JSON mode writes the error envelope and then the installed-SDK list, twice:

```
{ "schemaVersion": 1, "error": { "code": "analysis-failure", … } }
10.0.400 [/usr/local/share/dotnet/sdk]
…
```

Standard output is then no longer a single JSON document, which breaks the JSON contract that automation relies on (`validation/FINDINGS.md`, "Follow-up: stdout purity when the SDK can't be resolved").

The validation notes guessed that the list came from a child process. It doesn't. MSBuildLocator calls `hostfxr_resolve_sdk2` in-process, and when resolution fails, native hostfxr prints the SDK list straight to the process's standard output. MSBuildLocator captures the error text through `hostfxr_set_error_writer`, but not that list. Managed code can't intercept it by redirecting `Console.Out`.

## What Changes

- Before registering MSBuild, when a `global.json` exists in the target's directory or in one of its ancestors, the CLI runs `dotnet --version` as a child process in the target's directory, with standard output and standard error captured. The `dotnet` muxer uses the same resolver as hostfxr, so its result matches what MSBuildLocator would find.
- If that command fails with the "A compatible .NET SDK was not found" message, the CLI reports the existing actionable analysis failure (exit `2`), built from the captured text, and never calls MSBuildLocator. Nothing else reaches standard output.
- If the command succeeds, can't be started, or fails for any other reason, the CLI proceeds exactly as today.
- Targets with no `global.json` skip the pre-check, so they pay no extra process start.

## Capabilities

### Modified Capabilities

- `cli-analysis`: the "Target pins a missing SDK" scenario now also requires standard output in JSON mode to hold only the error envelope.

## Impact

- **Code:** `MsBuildRegistration` gains the pre-check and a way to locate the `dotnet` host, mirroring MSBuildLocator's order: `DOTNET_HOST_PATH`, the current process when it is `dotnet`, `DOTNET_ROOT`, then `PATH`.
- **Tests:** an out-of-process end-to-end test that runs the built CLI against a target pinning a missing SDK, and asserts that stdout parses as one JSON document.
- **Docs:** CHANGELOG (Unreleased); update the follow-up in `validation/FINDINGS.md`.
