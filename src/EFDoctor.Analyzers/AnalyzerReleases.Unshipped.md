; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD032 | Correctness | Warning | Detect a property change to an entity loaded with AsNoTracking that is followed by SaveChanges on the same DbContext without attaching the entity.
