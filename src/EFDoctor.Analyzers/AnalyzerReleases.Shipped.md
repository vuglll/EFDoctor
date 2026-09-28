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
