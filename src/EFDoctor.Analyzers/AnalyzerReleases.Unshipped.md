; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD028 | Correctness | Warning | Detect a method declared in the analyzed source that reads the query lambda parameter inside a proven EF Core query predicate or ordering.
