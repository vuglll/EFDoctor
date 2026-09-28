; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD009 | Performance | Warning | Detect ToLower or ToUpper applied to a mapped column in an EF Core predicate, which prevents an index seek on the column.
EFD014 | Correctness | Warning | Detect EF Core Skip pagination with no preceding ordering operator, producing non-deterministic results.
EFD023 | Performance | Info | Detect Contains, EndsWith, or a leading-wildcard EF.Functions.Like on a mapped column in an EF Core predicate.
EFD025 | Maintainability | Info | Detect EF Core Include paths that duplicate, or are covered by, another Include path in the same query.
EFD021 | Reliability | Warning | Detect an EF Core DbContext held in a static field, which is not thread-safe and outlives the intended unit of work.
EFD022 | Correctness | Warning | Detect a StringComparison string overload inside an EF Core query predicate, which EF Core cannot translate to SQL.
EFD020 | Performance | Warning | Detect an EF Core IQueryable local enumerated more than once, re-executing the query each time.
EFD010 | Performance | Warning | Detect a synchronous EF Core database call inside an async method that has a direct EF async counterpart.
