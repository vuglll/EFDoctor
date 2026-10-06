# EFDoctor

Local-first performance and safety diagnostics for EF Core codebases.

EFDoctor loads your solution with Roslyn and reports a small number of evidence-based findings, such as `SaveChanges` inside a loop, a query materialized before filtering, or an EF Core task that is never awaited. Each finding has a precise source location, a confidence level, the evidence that matched, the likely impact, and a practical fix. Nothing leaves your machine: there is no telemetry, network call, or source upload.

> **0.x.** EFDoctor is usable today, but rules, options, and output may still change before 1.0.

## Install

```bash
dotnet tool install --global EFDoctor
```

EFDoctor needs the .NET 10 runtime or later, and a .NET SDK that can build the project you analyze.

## Run

Restore or build the target first, then:

```bash
efdoctor analyze path/to/App.sln
efdoctor analyze path/to/App.csproj --format json --quiet
```

| Exit code | Meaning |
|---:|---|
| `0` | No findings |
| `1` | One or more findings |
| `2` | Invalid input or the target could not be analyzed |

A project that uses EF Core but whose EF Core types don't resolve (not restored, or a failed design-time build) isn't analyzed, and EFDoctor warns about it on standard error. If no EF Core project resolves, the run exits with `2` instead of reporting a clean result.

Loading a project runs its MSBuild logic, so analyze only repositories you trust.

## Rules

| Rule | Finding |
|---|---|
| EFD001 | `SaveChanges`/`SaveChangesAsync` inside a loop |
| EFD002 | `Count`/`CountAsync` used only to test existence, or `FirstOrDefault` used only as a null check |
| EFD003 | Foreign key without a covering index in a SQL Server or PostgreSQL model snapshot |
| EFD004 | Query materialized before filtering, projection, ordering, or paging |
| EFD005 | `ToList`/`ToListAsync` without a recognized row bound |
| EFD006 | Multiple sibling collection `Include`s that may cause cartesian explosion |
| EFD009 | `ToLower()`/`ToUpper()` on a column in a predicate, which prevents an index seek |
| EFD010 | Synchronous EF Core database call (`ToList`, `Count`, `SaveChanges`, `Find`, …) inside an `async` method |
| EFD011 | `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on an EF Core async call |
| EFD012 | Dynamically built SQL passed to `FromSqlRaw`/`ExecuteSqlRaw` |
| EFD013 | Load/loop/save pattern that may suit `ExecuteUpdate`/`ExecuteDelete` |
| EFD014 | `Skip` pagination with no preceding `OrderBy`, producing non-deterministic pages |
| EFD017 | `Include` ignored by a later non-entity `Select` |
| EFD018 | EF Core async call whose task is discarded |
| EFD019 | Query materialized and then immediately reduced, such as `ToList().First()` |
| EFD020 | EF Core query enumerated more than once, re-running it each time |
| EFD021 | EF Core `DbContext` held in a `static` field |
| EFD022 | `StringComparison` overload in a query predicate that EF Core cannot translate |
| EFD023 | `Contains`, `EndsWith`, or leading-wildcard `Like` on a column (advisory) |
| EFD025 | Duplicate or already-covered `Include` path in the same query (Info) |
| EFD027 | Concurrent EF Core operations on the same `DbContext`, such as `Task.WhenAll` over two queries |
| EFD029 | Second `OrderBy` that discards an earlier ordering instead of `ThenBy` |
| EFD037 | Entities materialized into a local when only a few of their columns are read (advisory) |
| EFD038 | `ExecuteUpdate`/`ExecuteDelete` that leaves already-tracked entities of the same type stale |

The same rules are available as analyzers that run in every build and in the IDE: see the `EFDoctor.Analyzers` package.

Suppress an intentional finding with standard Roslyn mechanisms (`#pragma warning disable EFD001`, `.editorconfig`, or `[SuppressMessage]`) and record why.

## License

Apache-2.0. See the `NOTICE` and `THIRD-PARTY-NOTICES.md` files included in the package.
