; Shipped analyzer releases
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD001 | Performance | Warning | Detect EF Core SaveChanges calls inside loops.
EFD002 | Performance | Warning | Detect EF Core Count or CountAsync calls used only to test existence.
EFD003 | Performance | Warning | Detect foreign keys without a covering index or key in a supported EF Core relational model snapshot.
EFD004 | Performance | Warning | Detect EF Core queries materialized before directly composed SQL-capable work.
EFD005 | Performance | Warning | Detect EF Core list materialization without a recognized query-side row bound.
EFD006 | Performance | Warning | Detect multiple sibling collection Include paths that may create a cartesian explosion.
EFD011 | Performance | Warning | Detect direct synchronous blocking on supported EF Core asynchronous operations.
EFD012 | Security | Warning | Detect dynamically constructed SQL passed to supported EF Core raw-SQL operations.
EFD013 | Performance | Warning | Detect conservative EF Core load-loop-save patterns that may support set-based bulk update or delete operations.
EFD017 | Correctness | Warning | Detect EF Core Include paths ignored by a later Select projection that returns only non-entity values.
EFD018 | Correctness | Warning | Detect resolved EF Core asynchronous operations whose returned task is discarded instead of awaited.
EFD019 | Performance | Warning | Detect EF Core queries materialized with ToList or ToArray and immediately reduced on the client.

## Release 0.2.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD009 | Performance | Warning | Detect ToLower or ToUpper applied to a mapped column in an EF Core predicate, which prevents an index seek on the column.
EFD010 | Performance | Warning | Detect a synchronous EF Core database call inside an async method that has a direct EF async counterpart.
EFD014 | Correctness | Warning | Detect EF Core Skip pagination with no preceding ordering operator, producing non-deterministic results.
EFD020 | Performance | Warning | Detect an EF Core IQueryable local enumerated more than once, re-executing the query each time.
EFD021 | Reliability | Warning | Detect an EF Core DbContext held in a static field, which is not thread-safe and outlives the intended unit of work.
EFD022 | Correctness | Warning | Detect a StringComparison string overload inside an EF Core query predicate, which EF Core cannot translate to SQL.
EFD023 | Performance | Info | Detect Contains, EndsWith, or a leading-wildcard EF.Functions.Like on a mapped column in an EF Core predicate.
EFD025 | Maintainability | Info | Detect EF Core Include paths that duplicate, or are covered by, another Include path in the same query.

## Release 0.3.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD027 | Reliability | Warning | Detect an EF Core asynchronous operation that starts while another operation on the same DbContext instance is still pending.
EFD029 | Correctness | Warning | Detect an EF Core OrderBy or OrderByDescending that discards an earlier ordering in the same inline query chain instead of using ThenBy.

## Release 0.4.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD037 | Performance | Info | Detect an EF Core query materialized into a local when the method reads only a small subset of the entity's scalar properties.
EFD038 | Correctness | Warning | Detect an EF Core ExecuteUpdate or ExecuteDelete that runs after entities of the same type were loaded with tracking from the same DbContext instance.

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
EFD006 | Performance | Info | Performance | Warning | Medium-confidence findings are suggestions in a build.
EFD009 | Performance | Info | Performance | Warning | Medium-confidence findings are suggestions in a build.
EFD010 | Performance | Info | Performance | Warning | Medium-confidence findings are suggestions in a build.
EFD013 | Performance | Info | Performance | Warning | Medium-confidence findings are suggestions in a build.
EFD020 | Performance | Info | Performance | Warning | Medium-confidence findings are suggestions in a build.

## Release 0.5.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EFD028 | Correctness | Warning | Detect a method declared in the analyzed source that reads the query lambda parameter inside a proven EF Core query predicate or ordering.
EFD030 | Reliability | Warning | Detect a deferred EF Core query returned from inside the using scope that disposes its DbContext.

## Release 0.6.0

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
