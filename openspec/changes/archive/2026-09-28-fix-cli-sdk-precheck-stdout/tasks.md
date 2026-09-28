# Tasks

## 1. Implementation

- [x] 1.1 Add a `global.json` lookup that walks up from the target directory.
- [x] 1.2 Add dotnet host location: `DOTNET_HOST_PATH`, the process path when it is `dotnet`, `DOTNET_ROOT`, then `PATH`.
- [x] 1.3 Add a `dotnet --version` pre-check with captured output and a timeout that fails open. On the SDK-not-found marker, return the existing `DescribeSdkResolutionFailure` message.
- [x] 1.4 Call the pre-check from `TryRegisterFor` before `MSBuildLocator` when a `global.json` applies.

## 2. Tests

- [x] 2.1 Add an out-of-process test that runs the built CLI in JSON mode against a temporary project whose `global.json` pins a missing SDK. Stdout parses as one JSON error envelope, exit is `2`, and stdout has no SDK-list line.
- [x] 2.2 Keep the existing in-process missing-SDK tests passing unchanged.
- [x] 2.3 Leave the new test untagged. `cli-analysis` isn't traced to tests, and tagging one scenario would require tagging all of them.

## 3. Documentation and validation

- [x] 3.1 Update the CHANGELOG (Unreleased) and the README prerequisites, and mark the follow-up resolved in `validation/FINDINGS.md`.
- [x] 3.2 Re-run jt-cleanarch, which pins a missing SDK, and confirm its recorded error is a single JSON envelope.

## 4. Verification

- [x] 4.1 Build with no warnings and run the full suite.
- [x] 4.2 Run `openspec validate fix-cli-sdk-precheck-stdout --strict`.
