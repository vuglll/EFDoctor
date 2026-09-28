# Design

## Context

Roslyn's BuildHost (`ProjectFile.GetProjectFileInfosAsync`) evaluates the project with no `TargetFramework`. When `TargetFramework` is empty and `TargetFrameworks` isn't, it runs `targetFrameworksValue.Split(';')` and builds once per piece, with the piece as the `TargetFramework` global property. It neither trims nor drops empty entries. MSBuild's own cross-targeting build turns the list into an item (`<_TargetFramework Include="$(TargetFrameworks)" />`), which trims.

## Decisions

### Normalize the property during evaluation, through a global-property hook

**Decision:** Pass `CustomAfterMicrosoftCommonCrossTargetingTargets=<tool dir>/build/EFDoctor.Workspace.targets` to `MSBuildWorkspace.Create`. `Microsoft.Common.CrossTargeting.targets` imports that path at the end of the outer evaluation. The file sets:

```xml
<TargetFrameworks>$([MSBuild]::Unescape($([System.Text.RegularExpressions.Regex]::Replace('$(TargetFrameworks)', '\s+', ''))))</TargetFrameworks>
<TargetFrameworks>$([MSBuild]::Unescape($([System.Text.RegularExpressions.Regex]::Replace('$(TargetFrameworks)', ';{2,}', ';').Trim(';'))))</TargetFrameworks>
```

**Why:**
- The fix runs inside the evaluation Roslyn reads. It needs no knowledge of which projects are affected, and it doesn't touch user files.
- The hook applies only to cross-targeting evaluations, so single-targeted projects and every inner build are untouched.

**Alternatives:**
- Pre-evaluate each project and pass a trimmed `TargetFrameworks` global property. Rejected: global properties are workspace-wide, but values differ per project.
- Write a `Directory.Build.targets` or an `obj/*.targets` file into the target. Rejected: EFDoctor must not modify the analyzed repository.
- Wait for a Roslyn fix. Rejected: it blocks real projects today. The hook is harmless once Roslyn trims.

### Unescape the normalized value

**Decision:** Wrap each property function in `$([MSBuild]::Unescape(…))`.

**Why:** MSBuild escapes property-function results, so `;` becomes `%3B`. Roslyn's `GetPropertyValue` unescapes the value, so the project itself still loads. But the SDK's `_ComputeTargetFrameworkItems` then turns the list into one item, `net48;net10.0`. A project that references such a project fails nearest-framework resolution with "targets 'net48;net10.0'. It cannot be referenced". OpenIddict hit exactly this. The single-framework fixture references the two-framework one to cover it.

### Chain to the default import

**Decision:** When the property is empty, the SDK defaults it to `$(MSBuildExtensionsPath)\v$(MSBuildToolsVersion)\Custom.After.Microsoft.Common.CrossTargeting.targets`, imported if it exists. Our file imports that same path, under the same `Exists` condition.

**Why:** Overriding the global property must not silently drop a machine-wide customization.

### Ship the file next to the assembly

**Decision:** The file is a `None` item with `CopyToOutputDirectory`. `dotnet pack` includes it in the tool's `tools/<tfm>/any/build/`. `WorkspaceAnalyzer` finds it under `AppContext.BaseDirectory`. If the file is missing, as in a broken install, the CLI opens the workspace without the property rather than failing.
