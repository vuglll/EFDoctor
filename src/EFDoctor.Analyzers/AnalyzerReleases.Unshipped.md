; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD037 | Performance | Info | Detect an EF Core query materialized into a local when the method reads only a small subset of the entity's scalar properties.

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
EFD006 | Performance | Info | Performance | Warning | Medium-confidence findings are suggestions in a build.
EFD009 | Performance | Info | Performance | Warning | Medium-confidence findings are suggestions in a build.
EFD010 | Performance | Info | Performance | Warning | Medium-confidence findings are suggestions in a build.
EFD013 | Performance | Info | Performance | Warning | Medium-confidence findings are suggestions in a build.
EFD020 | Performance | Info | Performance | Warning | Medium-confidence findings are suggestions in a build.
