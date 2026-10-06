# Development

This page is for working on EFDoctor itself. For the contribution workflow, including OpenSpec, the rule checklist, and branches, see [`CONTRIBUTING.md`](../CONTRIBUTING.md).

## Build and test

EFDoctor requires the .NET 10 SDK. From a clean checkout, restore once and then build and test without additional restore operations:

```bash
dotnet --version
dotnet restore EFDoctor.sln
dotnet build EFDoctor.sln --no-restore
dotnet test EFDoctor.sln --no-restore
```

The analyzer project targets `netstandard2.0` and compiles against Roslyn 4.8, so that the .NET 8 SDK compiler and later can load the `EFDoctor.Analyzers` package. Don't use a Roslyn API newer than 4.8 in an analyzer; `RepositoryConsistencyTests` checks the reference. The CLI, shared contracts, fixtures, and tests target `net10.0`.

## Run from source

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

## Repository structure

```text
src/
  EFDoctor.Analyzers/     Roslyn analyzers, one per rule, plus shared EF query analysis; packed as EFDoctor.Analyzers
  EFDoctor.Core/          Finding model, ordering, and console/JSON renderers
  EFDoctor.Cli/           MSBuild workspace loading and the `analyze` command
tests/
  EFDoctor.Analyzers.Tests/
  EFDoctor.Cli.Tests/     Reporting tests and end-to-end CLI process tests
  Fixtures/               Buildable sample projects, one or more per rule
docs/
  roadmap.md              Candidate rules, priorities, and the quality bar
  usage.md                Commands, options, output, installation
  suppression.md          Suppressing intentional findings
  development.md          This page
  rules/                  One page per rule, plus the rule boundaries (README.md)
  assets/                 README demo and its renderer
openspec/
  specs/                  Current behavior contracts
  changes/archive/        Completed changes
validation/               Real-project corpus and the precision harness
```

Detection logic, shared reporting contracts, and CLI orchestration stay separated: analyzers only emit Roslyn diagnostics with the shared finding properties, and the CLI maps them into findings.

Every analyzer starts its compilation-start action with `EfAnalysisScope.Includes`, which skips test projects, and reports through `EfDiagnostic.Create`, which gives medium-confidence and advisory findings `Info` severity in a build. `RepositoryConsistencyTests` checks both.

## Verification map

- `tests/EFDoctor.Analyzers.Tests` covers semantic matching for EFD001 through EFD006, EFD009 through EFD014, EFD017 through EFD023, EFD025, EFD027, EFD029, EFD037, and EFD038, exact locations, multiple findings, curated negative fixtures, expression/scope/model boundaries, index coverage, downstream query-shape, row-bound, sibling-include, split-query, case-transformed-column, sync-database-call-in-async, direct-blocking, pagination-ordering, projection-shape, discarded-task, client-reduction, multiple-enumeration, static-context-field, untranslatable-StringComparison, leading-wildcard-search, redundant-include-path, replaced-ordering, concurrent-context-operation, entity-over-fetch, and stale-tracked-entity classification, overlap precedence, and standard suppression.
- `tests/EFDoctor.Analyzers.Tests/AnalyzerPackageTests.cs` covers what the rules share in a build: severity by confidence, configured severities, and skipping test projects.
- `tests/EFDoctor.Cli.Tests/PackagedAnalyzerTests.cs` packs `EFDoctor.Analyzers` and builds a project that references it through central package management, checking the package contents, warnings and suggestions, help links, and test-project handling.
- `tests/EFDoctor.Cli.Tests/FindingAndReportingTests.cs` covers the finding contract, deterministic ordering, console output, JSON schema version `1`, and no-color output.
- `tests/EFDoctor.Cli.Tests/EndToEndTests.cs` invokes the built CLI process against mixed-rule, EFD003 model-snapshot, EFD004 query-composition, EFD005 query-boundary, EFD006 sibling-include, EFD009 case-transform, EFD010 sync-database-call, EFD011 direct-blocking, EFD012 raw-SQL, EFD013 bulk-operation, EFD014 unordered-pagination, EFD017 projection, EFD018 discarded-task, EFD019 client-reduction, EFD020 multiple-enumeration, EFD021 static-context, EFD022 StringComparison, EFD023 leading-wildcard, EFD025 redundant-include, EFD027 concurrent-context, EFD029 replaced-ordering, EFD037 entity-over-fetch, EFD038 stale-tracked-entity, test-project downgrade, and clean fixture projects and verifies console/JSON results, suppression, overlap behavior, deterministic ordering, stable exit codes, broken-project handling, unresolved EF Core warnings, and the absence of product networking components.

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
15. `add-efd029-orderby-then-orderby` added EFD029, the first rule from the roadmap's "Next" tier built in the public repository. It reports a second `OrderBy` that discards an earlier ordering.
16. `add-efd027-concurrent-dbcontext-operations` added EFD027, which proves two operations share one `DbContext` instance by symbol and reports the one that starts while the other is pending.
17. `add-analyzer-nuget-package` packaged the rules as `EFDoctor.Analyzers`, compiled against Roslyn 4.8. It gave findings a build severity that follows their confidence, moved test-project skipping into the analyzers, and added a help link to every rule.
18. `add-efd037-entity-over-fetch` added EFD037, the first rule that follows a materialized *result* instead of a query: it reports entities loaded into a local when every use in the method reads only a few scalar properties.
19. `add-efd038-stale-tracked-entities` added EFD038, which reports a bulk operation that leaves tracked entities of the same type stale, with a second confidence tier when the method goes on to use them. It extracted EFD027's proof that two operations share one `DbContext` instance into `EfContextIdentity`.
20. `improve-efd019-stored-count` let EFD019 report a materialized result stored in a local that is used only for its count, and made it recommend `Any` when a count only tests existence. It shares EFD002's existence-comparison classifier.

The current behavior contract for every rule and for the CLI lives in `openspec/specs/`. The remaining candidate rules and their priorities are in the [roadmap](roadmap.md).
