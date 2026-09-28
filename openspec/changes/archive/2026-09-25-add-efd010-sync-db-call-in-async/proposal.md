# Proposal

## Why

Calling a synchronous EF Core database API — `ToList()`, `First()`, `Count()`, `SaveChanges()`, `Find()` — from inside an `async` method blocks the calling thread for the whole round trip instead of yielding it. On a server this ties up thread-pool threads under load and defeats the point of the async request pipeline, when an awaitable counterpart (`ToListAsync`, `SaveChangesAsync`, …) is right there. It is a common, statically detectable mistake with low false-positive risk: a resolved synchronous EF call in an async context that has a direct EF `*Async` equivalent.

## What Changes

- Add rule **EFD010**: report a synchronous EF Core database call inside an `async` method or `async` lambda when a direct EF async counterpart exists — a materializing/aggregating LINQ terminal over a proven `DbSet` query (`ToList`, `ToArray`, `ToDictionary`, `First`, `Single`, `Last`, `Count`, `LongCount`, `Any`, `All`, `Min`, `Max`, `Sum`, `Average`, `Contains`, `ElementAt`), `DbContext.SaveChanges`, or `DbSet.Find`.
- Anchor the finding on the synchronous call and name the suggested `*Async` replacement.
- Report at medium confidence. Only flag when the enclosing method or lambda is `async` (so an awaitable call is possible without restructuring).
- Register EFD010 in the CLI analyzer set so it flows through the existing reporting, ordering, suppression, privacy, test-project, and exit-code contracts.

## Capabilities

### New Capabilities

- `efd010-sync-db-call-in-async`: Detect a synchronous EF Core database call in an async context that has a direct async counterpart.

### Modified Capabilities

None.

## Impact

- New analyzer `src/EFDoctor.Analyzers/SyncDatabaseCallInAsyncAnalyzer.cs`, reusing public `EfQueryOperationAnalysis` helpers (`TryAnalyzeSource`, `NormalizeMethod`, `GetInvocationSource`, `IsOrDerivesFrom`) with no edits to shared analysis code.
- `src/EFDoctor.Cli/WorkspaceAnalyzer.cs`: add the analyzer to the analyzer set; `AnalyzerReleases.Unshipped.md`: register EFD010.
- New rule doc `docs/rules/EFD010.md`; README rule table and CHANGELOG updated.
- New analyzer fixtures and tests (`tests/Fixtures/EFD010.Sample`, `SyncDatabaseCallInAsyncAnalyzerTests`) plus a CLI end-to-end test.
- No JSON schema, CLI command surface, exit-code catalogue, network, or telemetry change; other rules are unaffected. EFD010 is distinct from EFD011 (which flags `.Result`/`.Wait()` blocking on an async call); EFD010 flags choosing the sync API in the first place. A synchronous call inside a non-async method that returns `Task` without the `async` keyword is a deliberate out-of-scope limitation for this version.
