# EFDoctor.Analyzers

Roslyn analyzers that find EF Core performance and correctness problems in every build and in the IDE.

They report evidence-based findings, such as `SaveChanges` inside a loop, a query materialized before filtering, a synchronous database call in async code, or an EF Core task that is never awaited. Everything runs inside the compiler: there is no telemetry, network call, or source upload.

These are the same rules as the [`EFDoctor`](https://www.nuget.org/packages/EFDoctor) command-line tool. Use the tool for a full report with evidence, likely impact, and remediation, or for JSON output in CI. Use this package to see findings while you write code.

> **0.x.** Rules and defaults may still change before 1.0.

## Install

```bash
dotnet add package EFDoctor.Analyzers
```

Or once for every project, with central package management:

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="EFDoctor.Analyzers" Version="x.y.z" />

<!-- Directory.Build.props -->
<ItemGroup>
  <PackageReference Include="EFDoctor.Analyzers" PrivateAssets="all" />
</ItemGroup>
```

The analyzers need the .NET 8 SDK or later, or Visual Studio 2022 17.8 or later. The project can target any framework.

## Severities

Each finding has a confidence, and its default severity follows it:

| Confidence | Default severity |
|---|---|
| High | `warning` (`suggestion` for the cleanup rule EFD025) |
| Medium | `suggestion` |
| Advisory | `suggestion` |

So only high-confidence findings can fail a build that treats warnings as errors. Change a rule's severity in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.EFD010.severity = warning   # raise a medium-confidence rule
dotnet_diagnostic.EFD023.severity = none      # turn a rule off
```

## Test projects

Test projects are skipped: a project with `IsTestProject` set to `true`, or one that references xunit, NUnit, the Visual Studio test platform, or `Microsoft.NET.Test.Sdk`. To analyze one anyway, set this in its project file:

```xml
<PropertyGroup>
  <EFDoctorAnalyzeTestProjects>true</EFDoctorAnalyzeTestProjects>
</PropertyGroup>
```

## Rules and suppression

The rules are listed with their confidence in the [project README](https://github.com/vuglll/EFDoctor#rules). Every diagnostic links to its [rule page](https://github.com/vuglll/EFDoctor/tree/main/docs/rules), which describes what triggers the rule, what deliberately doesn't, and the fix. Suppress an intentional finding with standard Roslyn mechanisms (`#pragma warning disable EFD001`, `.editorconfig`, or `[SuppressMessage]`), and record why.

## License

Apache-2.0. See the `NOTICE` file included in the package.
