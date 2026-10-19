# Proposal

## Why

EFD006 reports sibling collection includes without an explicit `AsSplitQuery` or `AsSingleQuery`. It ignores a context configured with `UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)`, which makes EF Core split every such query. In validation round 2, that caused 65 of EFD006's 68 findings:

- **ErsatzTV (59).** Every context registration sets split queries, in the web host and the scanner, not in the data project.
- **Kavita (6).** The data project sets split queries itself.

## What Changes

- EFD006 doesn't report when the compilation contains `UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)`, recognized by syntax. The scan runs lazily, only once a finding is ready.
- A new global analyzer option, `efdoctor.split_query_by_default`, says the same for configuration the compilation can't see. A `.globalconfig` can set it in builds.
- The CLI sets the option for every project when any C# project in the scan configures split queries, which covers the host-and-data-project layout.
- The CLI's test-project options wrapper becomes a general `CliAnalyzerOptions`.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd006-multiple-collection-include`: "Respect split-query and client boundaries" gains the split-query default.

## Impact

- **Code:** `EfSplitQueryConfiguration` and `MultipleCollectionIncludeAnalyzer` in the analyzers; `WorkspaceAnalyzer` and `CliAnalyzerOptions` in the CLI.
- **Tests:** analyzer tests for the configuration call and the option; a CLI end-to-end test on the new `Workspace.SplitQueryData` and `Workspace.SplitQueryHost` fixtures.
- **Docs:** `docs/rules/EFD006.md`, the rules README, `docs/usage.md`, and the CHANGELOG.
