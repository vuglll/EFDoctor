; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD029 | Correctness | Warning | Detect an EF Core OrderBy or OrderByDescending that discards an earlier ordering in the same inline query chain instead of using ThenBy.
