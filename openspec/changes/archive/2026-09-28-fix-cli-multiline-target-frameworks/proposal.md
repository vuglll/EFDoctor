# Proposal

## Why

MSBuildWorkspace can't load a multi-targeted project whose `TargetFrameworks` value contains whitespace or line breaks. OpenIddict's `Directory.Build.props` defines its framework list this way:

```xml
<NetCoreTargetFrameworks>
  net10.0
</NetCoreTargetFrameworks>
```

`dotnet build` accepts this, because MSBuild splits the list into items and trims each one. Roslyn's workspace build host (`Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9) instead splits the raw property on `;` and passes each untrimmed piece as the `TargetFramework` global property. The design-time build then fails with "The TargetFramework value '…net10.0…' was not recognized", and the compilation has no references.

Since `add-cli-ef-resolution-check`, EFDoctor reports this as an unanalyzable project instead of a silent clean result. That makes the failure visible, but it doesn't fix it. OpenIddict still can't be analyzed (`validation/FINDINGS.md`, "Follow-up: MSBuildWorkspace and multi-line `TargetFrameworks`").

## What Changes

- The CLI opens every target with the MSBuild global property `CustomAfterMicrosoftCommonCrossTargetingTargets` set to a targets file that ships with the tool.
- That file is imported only by a multi-targeted project's outer evaluation, which is the one Roslyn reads `TargetFrameworks` from. It removes all whitespace from `TargetFrameworks`, collapses repeated `;`, and trims leading and trailing `;`. A target framework moniker never contains whitespace, so this changes nothing for a valid list.
- The file then imports whatever the SDK would have imported by default for that property, so a machine-wide `Custom.After.Microsoft.Common.CrossTargeting.targets` still applies.
- Single-targeted projects (`TargetFramework`) never import the file and are unaffected.

## Capabilities

### New Capabilities

- `cli-workspace-loading`: how the CLI works around MSBuildWorkspace load limitations so that projects `dotnet build` accepts are analyzed. It starts with multi-targeted projects whose `TargetFrameworks` list contains whitespace. This is a separate capability, not a `cli-analysis` requirement, so its scenarios can be traced to tests without tagging all of `cli-analysis`.

## Impact

- **Code:** a new `build/EFDoctor.Workspace.targets` content file in the CLI project, packed into the tool, and `WorkspaceAnalyzer` passes the global property to `MSBuildWorkspace.Create`.
- **Tests:** an end-to-end test with a temporary multi-targeted EF project whose `TargetFrameworks` spans several lines, verifying that findings are reported and that stderr has no warning.
- **Docs:** CHANGELOG (Unreleased) and a README note on analyzed-target prerequisites.
- **Validation:** re-run OpenIddict and record its findings.
- **Limitation:** a project that sets `CustomAfterMicrosoftCommonCrossTargetingTargets` itself has its value overridden during analysis. This is rare, and it affects only the outer, cross-targeting evaluation.
