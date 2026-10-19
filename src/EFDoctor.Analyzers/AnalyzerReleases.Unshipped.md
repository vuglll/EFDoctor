; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD031 | Correctness | Warning | Detect a LINQ operator composed over a FromSqlRaw, FromSql, or FromSqlInterpolated stored-procedure call.
EFD032 | Correctness | Warning | Detect a property change to an entity loaded with AsNoTracking that is followed by SaveChanges on the same DbContext without attaching the entity.
EFD033 | Reliability | Warning | Detect a singleton or hosted-service registration that captures a scoped DbContext for the application's lifetime.
EFD034 | Correctness | Warning | Detect Database.EnsureCreated on a DbContext type that the same compilation manages with migrations.
EFD036 | Performance | Info | Detect an EF Core query predicate whose filter columns lead no index or key in the model snapshot.
