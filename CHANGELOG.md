# Changelog

All notable changes to EFDoctor are documented here. Versions follow [Semantic Versioning](https://semver.org/).

## Unreleased

### Added

- EFD029: a second `OrderBy` or `OrderByDescending` that discards an earlier ordering in the same query, where `ThenBy` was meant. EF Core translates only the last `OrderBy`, so the query sorts by the replacing key alone. Reported at `Warning` severity with high confidence under the `Correctness` category. A second ordering after `Skip`, `Take`, `Distinct`, or a projection, and a default ordering overridden through a local, are not reported.

## 0.2.0

The first release built from the public repository, and the first stable 0.x release: install it with `dotnet tool install --global EFDoctor`, without `--prerelease`. Rules, options, and output may still change before 1.0; see the [roadmap](docs/roadmap.md).

### Changed

- The tool package includes repository and project URLs, with Source Link, when it is built with the `EFDoctorRepositoryUrl` property set. By default it still includes none.
- Queries stored in a local variable are now analyzed. Every rule that proves an EF Core query chain follows a local whose value is statically determined, and analyzes `var query = …; query = query.OrderBy(…); query.ToList()` like the equivalent inline query. The affected rules are EFD004, EFD005, EFD006, EFD009, EFD010, EFD013, EFD014, EFD017, EFD019, EFD020, EFD022, EFD023, and EFD025. Evidence names the followed local. A local reassigned inside an `if`, a loop, or a lambda is not followed. EFD006 and EFD025 still report each finding once when includes are split across a local.
- In JSON mode, standard output now holds only the error envelope when no installed SDK satisfies the target's `global.json`. Before, the .NET host appended the installed-SDK list to standard output. When a `global.json` governs the target, EFDoctor now checks SDK resolution first by running `dotnet --version` in the target's directory, with its output captured.
- The CLI now analyzes a multi-targeted project whose `TargetFrameworks` list is written across several lines, as in OpenIddict. Before, Roslyn's MSBuildWorkspace passed each untrimmed entry as the target framework, the design-time build failed, and the project couldn't be analyzed. EFDoctor normalizes the list during loading through a targets file that ships with the tool, and never modifies the analyzed repository.
- The CLI no longer reports a misleading clean result when EF Core types can't be resolved. A project that uses EF Core but can't resolve `DbContext`, because it isn't restored or its design-time build failed, gets an `EFDoctor warning` on standard error. If no EF Core project can be analyzed, the run exits with `2`.
- Queries that use `AsTracking()`, `IgnoreQueryFilters()`, or `IgnoreAutoIncludes()` are now analyzed. Before, these methods broke the inline query-chain proof, so EFD004, EFD005, EFD009, EFD010, EFD013, EFD014, EFD019, EFD020, EFD022, and EFD023 silently skipped every query that used them.
- EFD006 no longer reports a query that calls `AsSingleQuery()` explicitly. As with `AsSplitQuery()`, it records a deliberate loading choice, and EF Core itself stays silent in that case.
- EFD010 no longer reports the synchronous arm of an explicit sync/async switch, such as `async ? await q.FirstOrDefaultAsync(p) : q.FirstOrDefault(p)`, or an `if`/`else` whose other branch awaits the same EF counterpart.
- EFD020 now counts only real re-executions. A use inside another query's expression-tree lambda (for example `Where(x => ids.Contains(x.Id))`) and a predicate terminal (for example `Any(x => …)`) no longer count as executions, and executions in the two arms of the same conditional count as one path.
- EFD001 no longer reports a save that runs once per batch. That covers a loop over chunks (`Chunk(n)`), a pager loop whose condition declares the page, a loop that loads a page and iterates it before saving, and a threshold flush such as `if (processed >= BatchSize)`. A per-item save, including one nested inside a batch loop, is still reported.
- EFD005 proves keys from fluent configuration in the analyzed project (single-column `HasKey`, `HasAlternateKey`, `HasForeignKey`, and `HasIndex(...).IsUnique()`), and from EF Core's `<Navigation>Id` foreign-key convention. Queries filtered by such a key, for example loading the children of one known parent, are no longer reported.
- EFD005 reports a query whose only bound is equality on a key recognized by name (`Id`, `<Entity>Id`, `*Id`) at Advisory confidence and `Info` severity instead of Medium and `Warning`. The finding is still reported, because such a query usually loads one row or the children of one known parent.

### Added

- EFD009: `ToLower()` or `ToUpper()` applied to a column in an EF Core predicate, which prevents an index seek. Reported at `Warning` severity with medium confidence under the `Performance` category, with provider-aware remediation.
- EFD014: `Skip` pagination with no preceding `OrderBy`, producing non-deterministic pages.
- EFD021: EF Core `DbContext` held in a `static` field, which is not thread-safe and outlives the intended unit of work. Reported at `Warning` severity with high confidence under the `Reliability` category.
- EFD022: `StringComparison` string overload used inside an EF Core query predicate, which EF Core cannot translate to SQL and which throws at runtime. Reported at `Warning` severity with high confidence under the `Correctness` category.
- EFD020: an EF Core `IQueryable` local enumerated more than once in the same method, re-executing the query against the database each time. Reported at `Warning` severity with medium confidence under the `Performance` category.
- EFD010: a synchronous EF Core database call (`ToList`, `Count`, `SaveChanges`, `Find`, and peers) inside an `async` method or lambda where an EF `*Async` counterpart exists. Reported at `Warning` severity with medium confidence under the `Performance` category.
- EFD023: `Contains`, `EndsWith`, or a leading-wildcard `EF.Functions.Like` on a column in an EF Core predicate, which cannot seek an index. Reported at advisory confidence and `Info` severity under the `Performance` category, with provider-aware remediation.
- EFD025: `Include` path that duplicates, or is already covered by, another `Include` path in the same query. Reported at `Info` severity with high confidence under the `Maintainability` category.

### Fixed

- EFD005 no longer reports a false positive when a key-equality predicate compares an entity key against a property of another object (for example `Where(sa => sa.ProductId == product.ProductId)`). Such a comparand is now recognized as a bounding scalar, so the canonical "hydrate children for a known parent" query is no longer flagged as unbounded. A comparison between two properties of the filtered entity itself remains unbounded.
- EFD005 recognizes key equality written with a type's own `==` operator, such as `Guid`, `DateTimeOffset`, and their nullable forms, or with `.Equals()`. Before, these were reported as unbounded even when the key was proven.

## 0.1.0-preview.2

### Changed

- Test projects are now skipped by default. Use `--include-test-projects` to analyze them; when included, their findings are reported at Advisory confidence and `Info` severity (the previous default behavior).

### Fixed

- Analysis no longer aborts with "MSBuild could not load the target" on non-fatal MSBuild workspace load diagnostics (for example build warnings, unresolved optional imports, or a partially-failing sibling project). EFDoctor now analyzes every project that still produces a compilation and fails only when no analyzable compilation loads.

## 0.1.0-preview.1

First preview, packaged as the `EFDoctor` .NET tool (command `efdoctor`).

### Rules

- EFD001: `SaveChanges`/`SaveChangesAsync` inside a loop.
- EFD002: `Count`/`CountAsync` used only to test existence.
- EFD003: Foreign key without a covering index in a SQL Server or PostgreSQL model snapshot.
- EFD004: Query materialized before SQL-capable filtering, projection, ordering, or paging.
- EFD005: `ToList`/`ToListAsync` without a recognized row bound.
- EFD006: Multiple sibling collection `Include` paths that may cause cartesian explosion.
- EFD011: Synchronous blocking on an EF Core async operation.
- EFD012: Dynamically constructed SQL passed to an EF Core raw-SQL API.
- EFD013: Load/loop/save pattern that may support `ExecuteUpdate`/`ExecuteDelete`.
- EFD017: `Include` ignored by a later non-entity projection.
- EFD018: EF Core async operation whose task is discarded.
- EFD019: Query materialized and then immediately reduced.

### CLI

- `efdoctor analyze <solution-or-project>` with console output or JSON output (`schemaVersion: 1`), `--quiet`, and `--no-color`.
- Exit codes: `0` no findings, `1` findings, `2` invalid input or analysis failure.
- `--version` and `--help`.
- The .NET SDK is resolved from the analyzed target's directory, so its `global.json` is honored regardless of where the tool runs. An unsatisfiable `global.json` produces an actionable error.
- Findings from test projects are downgraded to Info severity and Advisory confidence.
