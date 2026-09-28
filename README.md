# EFDoctor

[![CI](https://github.com/vuglll/EFDoctor/actions/workflows/ci.yml/badge.svg)](https://github.com/vuglll/EFDoctor/actions/workflows/ci.yml)

**Local-first performance and safety diagnostics for EF Core.**

EFDoctor is a multi-provider .NET command-line tool that analyzes EF Core codebases—and, later, may optionally analyze a live SQL Server connection—for performance and safety anti-patterns that commonly survive code review and become expensive in production. Most static rules are provider-agnostic; SQL Server is the most-validated provider, and EFD003 also supports PostgreSQL through Npgsql.

The goal is not to produce a long checklist of generic advice. EFDoctor should report a small number of prioritized, evidence-based findings with low false-positive rates, precise source locations, confidence levels, clear explanations, and practical remediation guidance.

> **Project status:** validation MVP. EFD001 through EFD006, EFD009 through EFD014, EFD017 through EFD023, and EFD025 are implemented end to end through the local CLI.

## Principles

- **Local first:** source code and database information remain on the user's machine.
- **Evidence over guesses:** every finding must explain what matched and where.
- **Precision over rule count:** a few trusted rules are more useful than dozens of noisy warnings.
- **Context-aware guidance:** recommendations must acknowledge legitimate exceptions.
- **No premature infrastructure:** accounts, telemetry, hosted services, and IDE extensions are out of scope for now.

## Validation MVP

| Rule | Finding | Confidence target |
|---|---|---|
| **EFD001** | `SaveChanges` or `SaveChangesAsync` executed inside a loop | High |
| **EFD002** | `Count` or `CountAsync` used only to test existence instead of `Any` or `AnyAsync` | High |
| **EFD003** | Foreign-key property without a covering index or key in a supported SQL Server or PostgreSQL model snapshot | High |
| **EFD004** | Query materialized before filtering, projection, ordering, or paging that could run in SQL | High |
| **EFD005** | `ToList` or `ToListAsync` materializes an EF query without a recognized strong row bound | High / Medium / Advisory |
| **EFD006** | Multiple sibling collection `Include` paths that may cause cartesian explosion | Medium |
| **EFD009** | `ToLower()` or `ToUpper()` applied to a column in a predicate, which prevents an index seek | Medium |
| **EFD010** | Synchronous EF Core database call (`ToList`, `Count`, `SaveChanges`, `Find`, …) inside an `async` method | Medium |
| **EFD011** | `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` blocks on a supported EF Core async operation | High |
| **EFD012** | Dynamically constructed SQL passed to `FromSqlRaw`, `ExecuteSqlRaw`, or `ExecuteSqlRawAsync` | High |
| **EFD013** | Load/loop/save update or delete pattern that may support `ExecuteUpdate` or `ExecuteDelete` | Medium |
| **EFD014** | `Skip` pagination with no preceding `OrderBy`, producing non-deterministic pages | High |
| **EFD017** | `Include` or `ThenInclude` ignored by a later `Select` that projects only non-entity values | High |
| **EFD018** | EF Core async operation whose task is discarded instead of awaited | High |
| **EFD019** | Query materialized with `ToList`/`ToArray` and immediately reduced, such as `ToList().First()` or `ToArray().Count()` | High |
| **EFD020** | `IQueryable` local enumerated more than once, re-executing the query on each enumeration | Medium |
| **EFD021** | EF Core `DbContext` held in a `static` field, which is not thread-safe and lives for the whole process | High |
| **EFD022** | `StringComparison` overload in a query predicate that EF Core cannot translate to SQL (throws at runtime) | High |
| **EFD023** | `Contains`, `EndsWith`, or leading-wildcard `EF.Functions.Like` on a column, which cannot seek an index | Advisory |
| **EFD025** | `Include` path that duplicates, or is already covered by, another `Include` path in the same query (Info severity) | High |

Each rule must define its exact trigger, legitimate non-triggering cases, evidence, likely impact, remediation, confidence, and positive and negative test fixtures.

## Implementation history

Each rule was delivered as its own OpenSpec change, archived under `openspec/changes/archive/` with its proposal, design, spec deltas, and task list:

1. `bootstrap-efdoctor-and-efd001` established the shared architecture (analyzers, reporting contracts, CLI) and EFD001.
2. `add-efd002-count-existence-analysis` through `add-efd006-multiple-collection-include` added the remaining original query rules, EFD002 through EFD006.
3. `add-efd011-sync-over-async-analysis`, `add-efd012-unsafe-raw-sql`, and `add-efd013-bulk-update-delete-analysis` added the correctness and safety rules.
4. `add-efd017-projection-drops-include`, `add-efd018-unawaited-async-ef-call`, and `add-efd019-materialize-then-reduce` completed the recommended "Next" tier (EFD017 → EFD018 → EFD019).
5. `add-efd014-unordered-pagination` added EFD014, the non-deterministic pagination correctness rule.
6. `add-efd025-redundant-include` added the first cleanup rule, EFD025, sharing the include-path analysis with EFD017.
7. `add-efd021-static-dbcontext-field` added EFD021, the first object-lifetime rule, under the new `Reliability` category.
8. `add-efd009-non-sargable-case-transform` added EFD009, the first sargability rule, with provider-aware remediation.
9. `add-efd022-stringcomparison-in-predicate` added EFD022, which catches `StringComparison` overloads EF Core cannot translate.
10. `add-efd020-multiple-enumeration` added EFD020, the first rule that follows an `IQueryable` local across statements.
11. `add-efd010-sync-db-call-in-async` added EFD010, which flags a synchronous EF Core database call inside an async method where an `*Async` counterpart exists.
12. `add-efd023-leading-wildcard-search` added EFD023, the first advisory-only rule, and extracted the predicate scanning it shares with EFD009.
13. Follow-up changes refined existing rules: `improve-efd005-bound-model` added predicate-derived row bounds to EFD005, `improve-efd005-key-equality-scalar` let a key-equality bound compare against a property of another object (for example `child.ParentId == parent.Id`), and `broaden-efd003-relational-providers` extended EFD003 to PostgreSQL.
14. `improve-query-chain-local-tracking` let every rule that proves a query chain follow an `IQueryable` local whose value is statically determined. The rules affected are EFD004, EFD005, EFD006, EFD009, EFD010, EFD013, EFD014, EFD017, EFD019, EFD020, EFD022, EFD023, and EFD025.

The current behavior contract for every rule and for the CLI lives in `openspec/specs/`. The remaining candidate rules and their priorities are in the [roadmap](docs/roadmap.md).

## Planned technical direction

- .NET 10 LTS
- Roslyn analyzers
- EF Core semantic and model inspection
- Cross-platform `dotnet` CLI / global tool
- Human-readable console reports
- Versioned JSON output for automation
- Local-only execution with no product telemetry during validation

The analyzer project may target an analyzer-compatible framework independently of the .NET 10 CLI and test projects.

## Repository structure

```text
src/
  EFDoctor.Analyzers/     Roslyn analyzers, one per rule, plus shared EF query analysis
  EFDoctor.Core/          Finding model, ordering, and console/JSON renderers
  EFDoctor.Cli/           MSBuild workspace loading and the `analyze` command
tests/
  EFDoctor.Analyzers.Tests/
  EFDoctor.Cli.Tests/     Reporting tests and end-to-end CLI process tests
  Fixtures/               Buildable sample projects, one or more per rule
docs/
  roadmap.md              Candidate rules, priorities, and the quality bar
  rules/                  One page per rule: triggers, boundaries, remediation, suppression
openspec/
  specs/                  Current behavior contracts
  changes/archive/        Completed changes
validation/               Real-project corpus and the precision harness
scripts/                  Maintainer scripts, including the public export
```

Detection logic, shared reporting contracts, and CLI orchestration stay separated: analyzers only emit Roslyn diagnostics with the shared finding properties, and the CLI maps them into findings.

## Development workflow

EFDoctor uses [OpenSpec](https://github.com/Fission-AI/OpenSpec) to keep requirements, design decisions, scenarios, and implementation tasks aligned. The repository is already initialized; install the CLI to work with it:

```bash
npm install -g @fission-ai/openspec@latest
openspec list --specs        # current capabilities
openspec list                # in-flight changes
```

Each new rule or behavior change starts as a change under `openspec/changes/<name>/` with a proposal, spec deltas, a design, and a task list. Implementation starts after those artifacts are reviewed. When the work is complete, the spec deltas are synced into `openspec/specs/` and the change is archived.

## Validation standard

A rule is ready for the MVP only when:

- It has at least ten positive and ten negative fixtures.
- Curated negative fixtures have no known false positives.
- Initial real-project testing demonstrates at least 90% precision.
- Every finding points to concrete, actionable evidence.
- Intentional findings can be suppressed using a documented mechanism.

## Out of scope for the first milestone

- Live database diagnostics; any future live-connection feature is currently scoped to SQL Server
- Automatic code fixes
- Dapper or raw ADO.NET analysis
- IDE extensions
- CI/CD integration
- Accounts
- Hosted services or source uploads
- Product telemetry

## Build and test

EFDoctor requires the .NET 10 SDK. From a clean checkout, restore once and then build and test without additional restore operations:

```bash
dotnet --version
dotnet restore EFDoctor.sln
dotnet build EFDoctor.sln --no-restore
dotnet test EFDoctor.sln --no-restore
```

The analyzer project targets `netstandard2.0` for analyzer-host compatibility. The CLI, shared contracts, fixtures, and tests target `net10.0`.

## Run locally

Analyze an SDK-style C# project:

```bash
dotnet run --project src/EFDoctor.Cli/EFDoctor.Cli.csproj --no-restore -- \
  analyze tests/Fixtures/EFD001.Sample/EFD001.Sample.csproj --no-color
```

Analyze a `.sln`, `.slnx`, or `.csproj` target and emit versioned JSON:

```bash
dotnet run --project src/EFDoctor.Cli/EFDoctor.Cli.csproj --no-restore -- \
  analyze path/to/Target.sln --format json --quiet
```

Command shape:

```text
efdoctor analyze <solution-or-project-path> [--format console|json] [--quiet] [--no-color] [--include-test-projects]
```

`efdoctor --version` prints the tool version and `efdoctor --help` prints usage, options, and exit codes. To run `efdoctor` as an installed command instead of through `dotnet run`, see [Install as a .NET tool](#install-as-a-net-tool).

- Console output is the default and includes the rule, severity, confidence, source range, evidence, likely impact, remediation, and documentation key.
- `--format json` reserves standard output for JSON and implies no color.
- `--quiet` suppresses nonessential progress or decoration but does not suppress the requested report or errors.
- `--no-color` removes ANSI color sequences for automation.
- `--include-test-projects` analyzes test projects too. By default they are skipped; when included, their findings are reported at Advisory confidence and `Info` severity.
- Findings are ordered by normalized source path, start line/column, end line/column, and rule ID.

### Exit codes

| Code | Meaning |
|---:|---|
| `0` | Analysis completed successfully with no findings. |
| `1` | Analysis completed successfully with one or more findings. |
| `2` | The input was invalid or analysis could not complete. |

Exit code `1` is a successful scan result, not an EFDoctor execution failure.

### Projects whose EF Core types don't resolve

Every rule needs EF Core types. A project whose source uses EF Core (a `using Microsoft.EntityFrameworkCore…` directive), but whose compilation can't resolve `DbContext`, would otherwise look clean. That happens when the project isn't restored, or when its design-time build fails. EFDoctor doesn't analyze such a project. Instead:

- It writes `EFDoctor warning: …` to standard error, naming the project and including up to three of its load diagnostics. This happens in console and JSON modes alike, and even with `--quiet`. Standard output and the JSON schema are unchanged.
- If no project that uses EF Core could be analyzed, the run fails with exit code `2` instead of reporting no findings.

### Loading multi-targeted projects

Roslyn's MSBuildWorkspace splits a project's `TargetFrameworks` list on `;` without trimming, so a list written across several lines fails its design-time build even though `dotnet build` accepts it. EFDoctor works around this without touching the analyzed repository: it loads targets with the MSBuild global property `CustomAfterMicrosoftCommonCrossTargetingTargets` pointing to `build/EFDoctor.Workspace.targets`, which ships with the tool. That file removes whitespace and empty entries from `TargetFrameworks` in a multi-targeted project's outer evaluation, then imports the SDK's default file for that hook if one exists. Single-targeted projects never import it. A project that sets `CustomAfterMicrosoftCommonCrossTargetingTargets` itself has its value replaced during analysis.

### JSON schema version 1

JSON reports contain numeric `schemaVersion: 1`, a `summary.findingCount`, and an ordered `findings` array. Every finding includes `ruleId`, `ruleTitle`, `severity`, `confidence`, `message`, `sourceFile`, one-based `range` coordinates, `evidence`, `likelyImpact`, `suggestedRemediation`, and `documentationReference`.

Invalid-input and analysis failures requested in JSON mode use the same schema version and an `error` object with stable `code` and `message` fields.

## Install as a .NET tool

EFDoctor is published on nuget.org as the `EFDoctor` .NET tool, with the command `efdoctor`. Install it globally, so `efdoctor` works from any directory:

```bash
dotnet tool install --global EFDoctor
efdoctor --version
efdoctor analyze path/to/App.sln
```

Global tools are installed in `~/.dotnet/tools` (`%USERPROFILE%\.dotnet\tools` on Windows). If `efdoctor` is not found, add that directory to your `PATH`.

Or install it as a local tool, pinned in a repository's tool manifest (`dotnet-tools.json`):

```bash
dotnet new tool-manifest
dotnet tool install EFDoctor
dotnet efdoctor analyze App.sln
```

Update or remove it:

```bash
dotnet tool update --global EFDoctor
dotnet tool uninstall --global EFDoctor
```

### Build and install from source

To try unreleased changes, pack the tool from a checkout and install it from the output folder:

```bash
dotnet pack src/EFDoctor.Cli/EFDoctor.Cli.csproj -c Release -o artifacts
# creates artifacts/EFDoctor.<version>.nupkg and a matching .snupkg symbols package
dotnet tool update --global EFDoctor --add-source ./artifacts
```

NuGet caches each package version, so a rebuilt package with an unchanged `<Version>` is not picked up by `install` or `update`. Bump the version in `src/EFDoctor.Cli/EFDoctor.Cli.csproj`, or delete `~/.nuget/packages/efdoctor/<version>`, before reinstalling.

Prerequisites for the machine that runs the tool:

- The .NET 10 runtime or later.
- A .NET SDK that can build the analyzed project. The SDK is resolved from the target's own directory, so a `global.json` in the target repository is honored no matter where you run `efdoctor`. If no installed SDK satisfies it, EFDoctor exits with code `2` and names the SDK version to install. In JSON mode, standard output then holds only the error envelope.
- A target that has already been restored or built. EFDoctor never runs `restore`.
- Only analyze repositories you trust, because loading a project runs its MSBuild logic (see [Local-only operation and trust boundary](#local-only-operation-and-trust-boundary)).

The package declares the Apache-2.0 license and includes `NOTICE`, [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for the bundled Roslyn and runtime libraries, and [`CHANGELOG.md`](CHANGELOG.md). The package carries repository and project URLs, and Source Link, only when the build property `EFDoctorRepositoryUrl` is set; by default it carries none.

## Suppress an intentional finding

Use standard Roslyn suppression. Always record why the reported pattern is intentional.

For the smallest source region, use a pragma:

```csharp
#pragma warning disable EFD001 // Intentional per-item transaction boundary.
foreach (var item in items)
{
    context.SaveChanges();
}
#pragma warning restore EFD001
```

For a file or directory scope, use `.editorconfig` and place the justification in a nearby comment or engineering record:

```ini
[IntentionalPerItemWriter.cs]
# Reviewed: each item must commit independently for partial-failure isolation.
dotnet_diagnostic.EFD001.severity = none
```

Where the Roslyn host supports it, use `SuppressMessage` with its `Justification` property:

```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "EFD001",
    Justification = "Each iteration is an intentional independent transaction.")]
void SaveIndependently(DbContext context, IEnumerable<Item> items)
{
    foreach (var item in items)
    {
        context.SaveChanges();
    }
}
```

EFDoctor has no proprietary suppression file.

`SuppressMessage` matches on the rule's category as well as its ID. Most rules use `Performance`; EFD012 uses `Security`, EFD014, EFD017, EFD018, and EFD022 use `Correctness`, EFD021 uses `Reliability`, and EFD025 uses `Maintainability`. Each rule page shows the exact attribute.

For EFD002, use the same mechanisms with diagnostic ID `EFD002`. Examples and guidance for positive, empty-set, synchronous, and asynchronous existence checks are in [`docs/rules/EFD002.md`](docs/rules/EFD002.md).

For EFD003, use diagnostic ID `EFD003` and record why the index is intentionally absent from the EF model—for example, because an equivalent DBA-managed index was verified. See [`docs/rules/EFD003.md`](docs/rules/EFD003.md) for pragma, `.editorconfig`, and `SuppressMessage` examples.

For EFD004, use diagnostic ID `EFD004` and record why the client-side materialization boundary is intentional—for example, because a small bounded result is reused or the provider cannot translate required logic. See [`docs/rules/EFD004.md`](docs/rules/EFD004.md) for suppression examples.

For EFD005, use diagnostic ID `EFD005` and record why complete materialization is intentional—for example, for a controlled export or a known-small lookup table. See [`docs/rules/EFD005.md`](docs/rules/EFD005.md) for suppression examples.

For EFD006, use diagnostic ID `EFD006` and record why single-query loading is intentional or why split-query behavior is configured elsewhere. See [`docs/rules/EFD006.md`](docs/rules/EFD006.md) for suppression examples.

For EFD009, use diagnostic ID `EFD009` and record why the transformation is acceptable—for example, because an expression index on `LOWER(column)` backs the lookup. See [`docs/rules/EFD009.md`](docs/rules/EFD009.md) for suppression examples.

For EFD010, use diagnostic ID `EFD010` and record why the synchronous call is acceptable—for example, because it runs off the hot request path such as startup seeding or a single-threaded tool. See [`docs/rules/EFD010.md`](docs/rules/EFD010.md) for suppression examples.

For EFD011, use diagnostic ID `EFD011` and record why the synchronous integration boundary cannot propagate async. See [`docs/rules/EFD011.md`](docs/rules/EFD011.md) for suppression examples.

For EFD012, use diagnostic ID `EFD012` only after confirming that every dynamic fragment comes from a fixed allow-list rather than user input. See [`docs/rules/EFD012.md`](docs/rules/EFD012.md) for suppression examples.

For EFD013, use diagnostic ID `EFD013` and record why per-entity tracking is required—for example, because save interceptors, domain events, or concurrency tokens must run for each row. See [`docs/rules/EFD013.md`](docs/rules/EFD013.md) for suppression examples.

For EFD014, use diagnostic ID `EFD014` and record why an undefined row order is acceptable—for example, an intentionally unordered sample over a small table. See [`docs/rules/EFD014.md`](docs/rules/EFD014.md) for suppression examples.

For EFD017, use diagnostic ID `EFD017` and record why the include is kept even though the projection ignores it. See [`docs/rules/EFD017.md`](docs/rules/EFD017.md) for suppression examples.

For EFD018, use diagnostic ID `EFD018` and record why discarding the task is safe—for example, because it runs on a separately scoped context. See [`docs/rules/EFD018.md`](docs/rules/EFD018.md) for suppression examples.

For EFD019, use diagnostic ID `EFD019` and record why buffering the full result is acceptable—for example, for a known-tiny configuration table. See [`docs/rules/EFD019.md`](docs/rules/EFD019.md) for suppression examples.

For EFD020, use diagnostic ID `EFD020` and record why re-executing the query is acceptable—for example, because the second read must observe data changed by a save in between. See [`docs/rules/EFD020.md`](docs/rules/EFD020.md) for suppression examples.

For EFD021, use diagnostic ID `EFD021` and record why static state is acceptable—for example, a single-threaded console tool that disposes the context on exit. See [`docs/rules/EFD021.md`](docs/rules/EFD021.md) for suppression examples.

For EFD022, use diagnostic ID `EFD022` only when the query is deliberately materialized and the comparison runs on the client. See [`docs/rules/EFD022.md`](docs/rules/EFD022.md) for suppression examples.

For EFD023, use diagnostic ID `EFD023` and record why a scan is acceptable—for example, because the table is small or a trigram or full-text index already backs the search. To turn the advisory rule off entirely, set `dotnet_diagnostic.EFD023.severity = none` in `.editorconfig`. See [`docs/rules/EFD023.md`](docs/rules/EFD023.md) for suppression examples.

For EFD025, use diagnostic ID `EFD025` and record why the redundant include is kept—for example, to mirror a generated query template. To turn the cleanup rule off entirely, set `dotnet_diagnostic.EFD025.severity = none` in `.editorconfig`. See [`docs/rules/EFD025.md`](docs/rules/EFD025.md) for suppression examples.

## Query locals

Every rule that proves an EF Core query chain traces it from the operator it reports on back to a `DbSet<T>` or `DbContext.Set<TEntity>()`, through resolved `Queryable` and EF Core composition. That covers EFD004, EFD005, EFD006, EFD009, EFD010, EFD013, EFD014, EFD017, EFD019, EFD020, EFD022, EFD023, and EFD025. The chain may pass through a local variable when the local's value at that point is **statically determined**:

- the local is declared with an initializer, and every other write to it is a plain `query = …;` statement directly in the same block;
- it is never written through `ref`/`out`, a `ref` alias, deconstruction, or compound assignment;
- the read is in a later statement of that block, and not inside a lambda or local function.

The value is then the latest earlier write, or the initializer, so the rule analyzes the query as if it were written inline. Evidence that prints the chain names the local, for example `Queryable.Where -> local 'query' -> Queryable.OrderBy`.

```csharp
// Followed: every write is a plain statement in this block, so the ordering is known at Skip.
var query = context.Orders.Where(order => order.Active);
query = query.OrderBy(order => order.CreatedUtc);
var page = query.Skip(20).ToList();

// Not followed at any read: the composition at ToList depends on the path taken.
var recent = context.Orders.Where(order => order.Active);
if (onlyRecent) recent = recent.Where(order => order.CreatedUtc > cutoff);
var all = recent.ToList();
```

A local reassigned inside an `if`, a loop, or any other nested block is not followed at any read. Queries stored in fields or properties, or passed through parameters, return values, or helpers, are not followed either. EFD006 and EFD025 report at an include, so when includes are split across a local, each finding is still reported once.

## EFD001 control-flow boundary

EFD001 uses semantic method resolution plus lexical executable scope. It detects EF Core `SaveChanges` and `SaveChangesAsync` invocations inside `for`, `foreach`, `await foreach`, `while`, and `do`/`while`, including calls nested beneath ordinary blocks such as `if` and `try`. Loops that save once per batch are not reported. A loop saves once per batch when it iterates chunks (`foreach (var chunk in ids.Chunk(100))`), when a page is declared by its condition (`while (pager.ReadNextPage(out var page))`), when a page is loaded and iterated in its body, or when the save is a threshold flush such as `if (processed >= BatchSize)`.

The first version is intentionally not interprocedural: it does not follow helper calls, prove whether branches execute, or treat a lambda, anonymous function, or local function declared in an outer loop as part of that loop. It does detect a save surrounded by a loop inside the deferred callable itself. See [`docs/rules/EFD001.md`](docs/rules/EFD001.md) for the complete rule guidance.

## EFD002 expression boundary

EFD002 semantically identifies `Queryable.Count` expressions proven to originate from an EF Core `DbSet`, plus EF Core `CountAsync`. It reports direct zero/one comparisons used only for existence and recommends the equivalent `Any` or `AnyAsync` form.

The first version intentionally does not follow counts stored in variables, infer providers for arbitrary `IQueryable<T>` values, or report exact-count comparisons. See [`docs/rules/EFD002.md`](docs/rules/EFD002.md) for supported forms, exclusions, remediation, and suppression examples.

## EFD003 model-snapshot boundary

EFD003 semantically analyzes generated `ModelSnapshot.BuildModel` source when the project references an allow-listed provider whose database does not automatically create foreign-key indexes: SQL Server or PostgreSQL (Npgsql). A foreign key is covered when an index, primary key, or alternate key on the same entity—or an applicable base entity—starts with the complete ordered foreign-key property sequence. The finding evidence names the detected provider.

The rule does not run for known auto-indexing providers such as MySQL through Pomelo, or for unknown providers. It also does not reconstruct models from arbitrary `OnModelCreating` control flow, inspect historical migration operations, execute target code, or query a live database. A checked-in snapshot can be stale, and indexes managed outside EF migrations are not visible. Regenerate or review migrations and verify externally managed indexes before acting or suppressing. See [`docs/rules/EFD003.md`](docs/rules/EFD003.md) for the complete coverage contract and limitations.

## EFD004 expression boundary

EFD004 semantically identifies `ToList`, `ToArray`, `ToListAsync`, and `ToArrayAsync` calls whose inline query source is proven to originate from an EF Core `DbSet`. It reports when the buffered result is consumed immediately by a conservatively supported `Where`, `Select`, `OrderBy`, `OrderByDescending`, `Skip`, or `Take` operation.

It follows the query source through [query locals](#query-locals) but intentionally does not follow materialized values through locals or general data flow. It excludes explicit client-evaluation boundaries and downstream expressions containing custom calls, computed members, dynamic behavior, mutation, or user-defined operators. Review semantics and generated SQL before moving composition, especially where small bounded sets, result reuse, provider limitations, or tracking behavior justify client processing. See [`docs/rules/EFD004.md`](docs/rules/EFD004.md) for the complete rule contract.

## EFD005 query-boundary definition

EFD005 semantically identifies `ToList` and `ToListAsync` calls whose inline source is proven to originate from an EF Core `DbSet`. Strong bounds include a resolved query-side `Queryable.Take`, local-collection membership via `Contains` or `Any`, and equality on a key proven from data annotations, from fluent `HasKey`/`HasAlternateKey`/`HasForeignKey`/unique `HasIndex` configuration in the same project, or from EF Core's `<Navigation>Id` foreign-key convention. The value compared against the key may be any single value external to the filtered entity, including a property of another object (for example `child.ParentId == parent.Id`); the comparand kind does not affect whether the bound is recognized. Comparing two properties of the filtered entity itself is not a key-equality bound. Equality may use `==`, a type's own `==` operator (such as `Guid`'s), or `.Equals()`. Key-name heuristics and time-window predicates lower confidence to Advisory rather than suppressing. Projection, ordering, `Distinct`, `Skip`, `Include`, and tracking modifiers do not establish a maximum row count by themselves.

Confidence reflects the recognized bound and best-effort call-site context: clearly hot unbounded paths can be High, unknown context defaults to Medium, and key-by-name equality and time-window intent are Advisory. Test projects are skipped by default; with `--include-test-projects` their findings are centrally downgraded to Advisory/Info without being suppressed. The rule follows [query locals](#query-locals) whose value is statically determined, but no other stored query values, and it doesn't cross client-evaluation boundaries, and it yields to EFD004 and EFD019 at the same materializer. If every row is not required, review stable server-side paging, chunking, or purpose-built aggregates; full materialization remains legitimate for cases such as hydrating children for known parents. See [`docs/rules/EFD005.md`](docs/rules/EFD005.md) for full boundaries, exceptions, and suppression guidance.

## EFD006 include-chain boundary

EFD006 semantically identifies inline EF Core query chains with at least two distinct root-level collection `Include` properties. It reports once at the second collection include, recognizes filtered selectors and static syntax, and suppresses the finding when the same inline chain explicitly uses `AsSplitQuery` or `AsSingleQuery`, the two choices EF Core itself treats as acknowledging the trade-off.

The first version does not count reference navigations, duplicate paths, or collection paths found only beneath one `ThenInclude` branch, and it follows stored query values only through [query locals](#query-locals). It does not inspect global query-splitting configuration. Review generated SQL and expected cardinalities before choosing `AsSplitQuery`, projection, or separate queries because additional queries introduce round-trip and consistency trade-offs. See [`docs/rules/EFD006.md`](docs/rules/EFD006.md) for the complete contract.

## EFD009 case-transform boundary

EFD009 semantically identifies the parameterless `string.ToLower()` or `string.ToUpper()` applied to a mapped string column path inside the predicate of `Where` or a predicate-taking `Queryable` or EF Core async terminal, on an inline query traced to `DbSet<T>` or `DbContext.Set<TEntity>()` whose entity type matches the predicate parameter. It reports when the transformed column is compared with a value that doesn't reference the entity, using `==`, `!=`, `string.Equals`, `Equals`, or `StartsWith`, anchored on the transformation call with medium confidence.

Transformed comparison values, column-to-column comparisons, `Contains`/`EndsWith`, `StringComparison` overloads, invariant and culture overloads, computed properties, nested lambdas, projections, and unproven sources are not reported. The remediation leads with the referenced provider's option—removing the transformation under SQL Server's case-insensitive collations, or `citext`, `EF.Functions.ILike`, or an expression index on PostgreSQL—and an existing index on the transformed expression is a reason to suppress. See [`docs/rules/EFD009.md`](docs/rules/EFD009.md) for the complete contract.

## EFD010 sync-database-call boundary

EFD010 semantically identifies a synchronous EF Core database call inside an `async` method or `async` lambda that has a direct EF `*Async` counterpart: a materializing or aggregating LINQ terminal (`ToList`, `ToArray`, `ToDictionary`, `First`/`Single`/`Last` and their `OrDefault` forms, `Count`, `LongCount`, `Any`, `All`, `Min`, `Max`, `Sum`, `Average`, `Contains`, `ElementAt`, `ElementAtOrDefault`) whose source is traced to `DbSet<T>` or `DbContext.Set<TEntity>()`, `DbContext.SaveChanges`, or `DbSet.Find`. It anchors on the call, names the suggested `*Async` replacement, and reports at medium confidence. It skips the synchronous arm of an explicit sync/async switch, meaning a `?:` or `if`/`else` whose other arm already awaits the same EF counterpart.

Calls in non-async methods, calls that already use the `*Async` counterpart, terminals over in-memory sequences or unproven `IQueryable<T>` sources, and synchronous calls inside a synchronous lambda nested in an async method are not reported. EFD010 is complementary to EFD011, which flags blocking on an async call rather than choosing the synchronous API; the two do not double-report. See [`docs/rules/EFD010.md`](docs/rules/EFD010.md) for the complete contract.

## EFD011 direct-blocking boundary

EFD011 semantically identifies direct `.Result`, parameterless `.Wait()`, and `.GetAwaiter().GetResult()` consumption of supported EF Core asynchronous query terminals, `DbContext.SaveChangesAsync`, and `DbSet.FindAsync`. It recognizes configured awaiters, reports the complete blocking expression, and uses high confidence because both the EF source and BCL blocking API are resolved symbols.

The first version does not follow tasks through locals or general data flow, report timeout/cancellation `Wait` overloads, or diagnose unrelated async methods. Prefer `await` and propagate async through callers; when a truly synchronous host boundary cannot change, isolate and document it rather than adding another blocking wrapper. See [`docs/rules/EFD011.md`](docs/rules/EFD011.md) for the complete contract.

## EFD012 raw-SQL boundary

EFD012 semantically identifies interpolated strings and non-constant concatenation passed as the SQL argument of EF Core `FromSqlRaw`, `ExecuteSqlRaw`, or `ExecuteSqlRawAsync`, including through a local assigned exactly once in the same straight-line block. It reports high confidence because the dynamic SQL shape is proven; it does not claim that an input is attacker-controlled.

Constant SQL, placeholder arguments, and interpolated-safe APIs such as `FromSqlInterpolated` and `ExecuteSqlInterpolatedAsync` are not reported, and the rule does not follow fields, parameters, helpers, or cross-method flow. Pass values as parameters; when SQL structure such as a column name must vary, select it from a fixed allow-list. See [`docs/rules/EFD012.md`](docs/rules/EFD012.md) for the complete contract.

## EFD013 bulk-operation boundary

EFD013 identifies a materialized EF query consumed by a `foreach` that only assigns uniform values to scalar properties, or only removes each entity, immediately followed by a save on the same context. It reports medium confidence at the materializer and suggests `ExecuteUpdate`/`ExecuteDelete` (or their async forms) when the target's EF Core version exposes them.

Loops that read the current entity, branch, call other code, touch navigations, or save elsewhere are not reported. Bulk operations bypass change tracking, interceptors, and in-memory state, so review those effects before switching. See [`docs/rules/EFD013.md`](docs/rules/EFD013.md) for the complete contract.

## EFD014 unordered-pagination boundary

EFD014 semantically identifies `Queryable.Skip`, optionally followed by `Take`, on an inline query traced to `DbSet<T>` or `DbContext.Set<TEntity>()` with no `OrderBy`, `OrderByDescending`, `ThenBy`, or `ThenByDescending` earlier in the chain. It reports once per paging expression, anchored on the outer `Take` when present, with high confidence and `Correctness` category. An ordering applied after the paging operator does not count.

A bare `Take` without `Skip` is deliberately not reported, because top-N reads with no order are often intentional. Any recognized ordering suppresses the finding without judging key uniqueness, and conditionally composed query locals, arbitrary `IQueryable<T>` parameters, in-memory sources, and look-alike methods are outside the rule. Add a stable ordering, ideally on a unique key or with a deterministic tie-breaker, before `Skip`. See [`docs/rules/EFD014.md`](docs/rules/EFD014.md) for the complete contract.

## EFD017 projection boundary

EFD017 semantically identifies resolved EF Core `Include` and `ThenInclude` calls in an inline chain traced to `DbSet<T>` or `DbContext.Set<TEntity>()` and followed by a resolved `Queryable.Select`. It reports once, at the `Select`, when every projected leaf is definitely non-entity: scalar, enum, nullable value, or `string` values, and anonymous objects, tuples, or object constructions built only from them. The evidence lists every include path.

The rule stays silent for identity projections, wrappers containing the source entity, direct navigation or collection results, and other reference-typed values it can't classify without the runtime EF model. It follows [query locals](#query-locals) but not helpers, custom operators, or materialization boundaries, and it ignores string-based includes. Remove the redundant include when the projection is all the caller needs; return the entity when populated navigations are required. See [`docs/rules/EFD017.md`](docs/rules/EFD017.md) for the complete contract.

## EFD018 discarded-task boundary

EFD018 semantically identifies EF Core asynchronous operations—queryable-extension terminals and bulk operations, `SaveChangesAsync`, `FindAsync`, `AddAsync`/`AddRangeAsync`, and relational `ExecuteSql*Async`—whose task is discarded as a standalone statement, through an explicit `_ =` assignment, or as the body of a void-returning lambda, including after `ConfigureAwait(...)` or null-conditional access. It reports the complete discarded expression with high confidence.

Tasks that are awaited, returned, stored, passed on, composed, or synchronously blocked are not reported; blocking remains EFD011's domain. The rule complements the compiler's CS4014 warning by also covering synchronous methods and explicit discards. Await the operation and propagate async; for intentional background work, use a separately scoped context and observe the task. See [`docs/rules/EFD018.md`](docs/rules/EFD018.md) for the complete contract.

## EFD019 materialize-then-reduce boundary

EFD019 semantically identifies `ToList`, `ToArray`, or awaited `ToListAsync`/`ToArrayAsync` materializers over a proven inline EF query whose buffered result is immediately reduced with `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, ordered `Last`/`LastOrDefault`, `Any`, `All`, `Count`, `LongCount`, `Sum`, `Min`, `Max`, `Average`, or the `List<T>.Count`/array `Length` property. Predicate and selector lambdas must use the same SQL-capable shapes EFD004 accepts. The finding is anchored on the materializer, and EFD005 yields to it there.

Query-side reductions, explicit client boundaries, stored lists, arbitrary or in-memory sources, unsupported lambdas and overloads, and unordered `Last` are not reported. Apply the reducer to the query, or use its EF Core async counterpart such as `CountAsync`. See [`docs/rules/EFD019.md`](docs/rules/EFD019.md) for the complete contract.

## EFD020 multiple-enumeration boundary

EFD020 reports an `IQueryable<T>` local whose initializer is a proven EF Core query traced to a `DbSet` and that is executed two or more times in the same method, through `foreach` or a materializing or aggregating terminal such as `ToList`, `Count`, `Any`, `First`, `Sum`, or their async variants. The finding is anchored on the local's declaration with medium confidence and states how many executions were observed. Non-terminal composition such as `Where` or `Select` is not an execution. Two other uses aren't executions either: a use inside another query's expression-tree lambda, such as `Where(x => ids.Contains(x.Id))`, which EF composes into the same SQL statement; and a predicate terminal such as `Any(x => …)`, which runs a different query. Executions in the two arms of the same conditional count as one path.

A query executed at most once, a materialized `List<T>` reused many times, a local reassigned after its declaration, unproven or in-memory sources, and queries passed to or returned from other methods are not reported. Materialize the query once and reuse the result; suppress with a reason when a deliberate re-query must observe changed data. See [`docs/rules/EFD020.md`](docs/rules/EFD020.md) for the complete contract.

## EFD021 static-DbContext boundary

EFD021 reports a `static` field whose type resolves to EF Core `DbContext` or a derived type, anchored on the field declaration with high confidence. A `DbContext` is not thread-safe and is meant to live for one unit of work, so a process-lifetime shared instance leads to concurrency exceptions, stale first-level-cache reads, and unbounded change-tracker growth.

Instance fields, `const` and compiler-synthesized fields, non-`DbContext` types, and look-alike types that don't derive from EF Core's `DbContext` are not reported. `static` auto-properties and DI-singleton lifetimes are out of scope for this version. Register the context as scoped, use `IDbContextFactory<T>`, or create it in a short `using` block. See [`docs/rules/EFD021.md`](docs/rules/EFD021.md) for the complete contract.

## EFD022 StringComparison boundary

EFD022 reports a `System.String` method call that takes a `StringComparison` argument—`Equals`, `StartsWith`, `EndsWith`, `Contains`, `IndexOf`, or static `string.Equals`/`string.Compare`—inside a lambda passed to a `Queryable` operator whose inline source traces to `DbSet<T>` or `DbContext.Set<TEntity>()`. EF Core has no SQL translation for these overloads, so the query throws at runtime; the finding is anchored on the string-method call with high confidence under the `Correctness` category.

Plain comparisons without `StringComparison`, in-memory `Enumerable` queries, unproven `IQueryable<T>` parameters, calls outside query predicates, and calls nested inside an in-memory `Enumerable` operator within a predicate are not reported. Use a translatable comparison and rely on the column collation for case sensitivity, or materialize deliberately and suppress with a reason. See [`docs/rules/EFD022.md`](docs/rules/EFD022.md) for the complete contract.

## EFD023 leading-wildcard boundary

EFD023 semantically identifies `string.Contains(string)` or `string.EndsWith(string)` on a mapped string column—directly or after `ToLower()`/`ToUpper()`—and EF Core `EF.Functions.Like` whose pattern provably starts with `%` or `_` (a constant, the leftmost part of a concatenation, or leading interpolated text), inside the predicate of `Where` or a predicate-taking `Queryable` or EF Core async terminal on an inline query traced to `DbSet<T>` or `DbContext.Set<TEntity>()`. It reports once per search call at advisory confidence and `Info` severity.

Prefix searches, `Like` patterns of unknown shape, `StringComparison` overloads (EFD022) and `char` overloads, collection `Contains(column)`, column-to-column searches, computed properties, nested lambdas, projections, and unproven sources are not reported. A case transformation compared with `==`, `Equals`, or `StartsWith` belongs to EFD009, so each shape gets exactly one finding. The remediation leads with SQL Server full-text search or a PostgreSQL `pg_trgm` trigram index, and suggests `StartsWith` or a reversed column where the semantics allow. See [`docs/rules/EFD023.md`](docs/rules/EFD023.md) for the complete contract.

## EFD025 redundant-include boundary

EFD025 semantically identifies resolved EF Core `Include`/`ThenInclude` chains in one inline query traced to `DbSet<T>` or `DbContext.Set<TEntity>()`. It reports a chain whose full navigation path duplicates an earlier chain, or is a strict prefix of a longer chain anywhere in the same query. Paths are compared by resolved property symbol. Constant string paths are compared only with other string paths. Findings use `Info` severity and high confidence, and the earliest occurrence of every longest path is never reported, so deleting every reported chain keeps the loaded navigations unchanged.

A repeated leading `Include` that branches into a different `ThenInclude` is required syntax and is not reported. Filtered includes, casts, non-constant strings, string-versus-expression pairs, includes split across helpers or conditionally reassigned locals, element-type-changing operators, and model `AutoInclude()` configuration are outside the rule. See [`docs/rules/EFD025.md`](docs/rules/EFD025.md) for the complete contract.

## Local-only operation and trust boundary

EFDoctor includes no telemetry, HTTP client, update check, license validation, source upload, or hosted component. After the target and its dependencies are available locally, the CLI does not invoke restore and does not initiate product network calls.

Project loading uses Roslyn's MSBuild workspace. MSBuild can execute design-time targets authored by the project being analyzed, so only analyze repositories you trust. Local-first operation prevents EFDoctor from transmitting source or findings; it is not a sandbox for untrusted MSBuild logic. Restore or build the target yourself before analysis so its SDKs and dependencies are already present.

## Verification map

- `tests/EFDoctor.Analyzers.Tests` covers semantic matching for EFD001 through EFD006, EFD009 through EFD014, EFD017 through EFD023, and EFD025, exact locations, multiple findings, curated negative fixtures, expression/scope/model boundaries, index coverage, downstream query-shape, row-bound, sibling-include, split-query, case-transformed-column, sync-database-call-in-async, direct-blocking, pagination-ordering, projection-shape, discarded-task, client-reduction, multiple-enumeration, static-context-field, untranslatable-StringComparison, leading-wildcard-search, and redundant-include-path classification, overlap precedence, and standard suppression.
- `tests/EFDoctor.Cli.Tests/FindingAndReportingTests.cs` covers the finding contract, deterministic ordering, console output, JSON schema version `1`, and no-color output.
- `tests/EFDoctor.Cli.Tests/EndToEndTests.cs` invokes the built CLI process against mixed-rule, EFD003 model-snapshot, EFD004 query-composition, EFD005 query-boundary, EFD006 sibling-include, EFD009 case-transform, EFD010 sync-database-call, EFD011 direct-blocking, EFD012 raw-SQL, EFD013 bulk-operation, EFD014 unordered-pagination, EFD017 projection, EFD018 discarded-task, EFD019 client-reduction, EFD020 multiple-enumeration, EFD021 static-context, EFD022 StringComparison, EFD023 leading-wildcard, EFD025 redundant-include, test-project downgrade, and clean fixture projects and verifies console/JSON results, suppression, overlap behavior, deterministic ordering, stable exit codes, broken-project handling, unresolved EF Core warnings, and the absence of product networking components.

## Product background

Candidate rules, their priorities, and the quality bar a rule must meet are in the [roadmap](docs/roadmap.md). To contribute, see [`CONTRIBUTING.md`](CONTRIBUTING.md); to report a vulnerability, see [`SECURITY.md`](SECURITY.md).

## License

EFDoctor is licensed under the [Apache License, Version 2.0](LICENSE). Copyright 2026 Nemanja Markovic; see [`NOTICE`](NOTICE).
