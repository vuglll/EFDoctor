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

The analyzer project targets `netstandard2.0` for analyzer-host compatibility. The CLI, shared contracts, fixtures, and tests target `net10.0`.

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
  EFDoctor.Analyzers/     Roslyn analyzers, one per rule, plus shared EF query analysis
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

## Verification map

- `tests/EFDoctor.Analyzers.Tests` covers semantic matching for EFD001 through EFD006, EFD009 through EFD014, EFD017 through EFD023, EFD025, EFD027, and EFD029, exact locations, multiple findings, curated negative fixtures, expression/scope/model boundaries, index coverage, downstream query-shape, row-bound, sibling-include, split-query, case-transformed-column, sync-database-call-in-async, direct-blocking, pagination-ordering, projection-shape, discarded-task, client-reduction, multiple-enumeration, static-context-field, untranslatable-StringComparison, leading-wildcard-search, redundant-include-path, replaced-ordering, and concurrent-context-operation classification, overlap precedence, and standard suppression.
- `tests/EFDoctor.Cli.Tests/FindingAndReportingTests.cs` covers the finding contract, deterministic ordering, console output, JSON schema version `1`, and no-color output.
- `tests/EFDoctor.Cli.Tests/EndToEndTests.cs` invokes the built CLI process against mixed-rule, EFD003 model-snapshot, EFD004 query-composition, EFD005 query-boundary, EFD006 sibling-include, EFD009 case-transform, EFD010 sync-database-call, EFD011 direct-blocking, EFD012 raw-SQL, EFD013 bulk-operation, EFD014 unordered-pagination, EFD017 projection, EFD018 discarded-task, EFD019 client-reduction, EFD020 multiple-enumeration, EFD021 static-context, EFD022 StringComparison, EFD023 leading-wildcard, EFD025 redundant-include, EFD027 concurrent-context, EFD029 replaced-ordering, test-project downgrade, and clean fixture projects and verifies console/JSON results, suppression, overlap behavior, deterministic ordering, stable exit codes, broken-project handling, unresolved EF Core warnings, and the absence of product networking components.

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

The current behavior contract for every rule and for the CLI lives in `openspec/specs/`. The remaining candidate rules and their priorities are in the [roadmap](roadmap.md).
