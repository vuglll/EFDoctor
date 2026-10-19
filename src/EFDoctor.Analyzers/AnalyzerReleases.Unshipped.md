; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD015 | Performance | Info | Detect a Contains in an EF Core query over a key list materialized from another unbounded query on the same DbContext.
EFD016 | Reliability | Info | Detect an EF Core async operation called without the CancellationToken parameter that is in scope.
EFD024 | Performance | Info | Detect an Include of a collection navigation whose every use in the method is filtered in memory.
EFD026 | Performance | Info | Detect Update on an entity the same method loaded with tracking from the same DbContext.
EFD031 | Correctness | Warning | Detect a LINQ operator composed over a FromSqlRaw, FromSql, or FromSqlInterpolated stored-procedure call.
EFD032 | Correctness | Warning | Detect a property change to an entity loaded with AsNoTracking that is followed by SaveChanges on the same DbContext without attaching the entity.
EFD033 | Reliability | Warning | Detect a singleton or hosted-service registration that captures a scoped DbContext for the application's lifetime.
EFD034 | Correctness | Warning | Detect Database.EnsureCreated on a DbContext type that the same compilation manages with migrations.
EFD035 | Correctness | Warning | Detect an indexed nvarchar(max) column and a decimal left at the default precision in an EF Core model snapshot.
EFD036 | Performance | Info | Detect an EF Core query predicate whose filter columns lead no index or key in the model snapshot.
