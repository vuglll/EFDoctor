# Spec Delta

## Purpose

Define local, high-confidence detection of EF Core queries that page with `Skip`/`Take` but establish no row ordering, whose results are therefore non-deterministic across executions.

## ADDED Requirements

### Requirement: Detect unordered pagination
EFD014 SHALL report a proven inline EF Core query that pages with a resolved `System.Linq.Queryable.Skip` (optionally followed by `Take`) without a resolved ordering operator (`OrderBy`, `OrderByDescending`, `ThenBy`, or `ThenByDescending` on `System.Linq.Queryable`) earlier in the same inline chain. Pagination requires `Skip`: a chain whose only paging operator is `Take` is a top-N shape that is frequently intentional and SHALL NOT be reported. The source SHALL be traced through supported query composition to an EF Core `DbSet` or `DbContext.Set<TEntity>()`, using resolved method and type symbols rather than method-name text. EFD014 SHALL emit exactly one finding per paged query, anchored at the outermost paging operator, so a `Skip` immediately consumed by a `Take` yields a single finding on the `Take`.

#### Scenario: Skip without ordering
- **WHEN** an inline EF query applies `Skip(n)` over a proven `DbSet` source with no ordering operator earlier in the chain
- **THEN** EFD014 reports the paging operator

#### Scenario: Skip and Take without ordering
- **WHEN** an inline EF query applies `Skip(n).Take(m)` with no ordering operator earlier in the chain
- **THEN** EFD014 emits exactly one finding, anchored at the outer `Take`

#### Scenario: Take-only is not reported
- **WHEN** an inline EF query applies `Take(m)` with no `Skip` and no ordering operator
- **THEN** EFD014 does not report, because a bare top-N is frequently intentional

#### Scenario: Ordered pagination is not reported
- **WHEN** an inline EF query applies an ordering operator before `Skip`/`Take` (for example `OrderBy(...).Skip(n).Take(m)`)
- **THEN** EFD014 does not report

#### Scenario: In-memory or arbitrary source
- **WHEN** `Skip`/`Take` operate on an in-memory sequence or an `IQueryable<T>` whose inline source is not proven to originate from `DbSet`
- **THEN** EFD014 does not report

#### Scenario: Unrelated or unresolved operator
- **WHEN** a same-named `Skip`/`Take`/`OrderBy` resolves outside `System.Linq.Queryable`, or the operator does not resolve successfully
- **THEN** EFD014 does not report

### Requirement: Produce actionable and qualified EFD014 findings
Each EFD014 finding SHALL have rule title `Pagination has no deterministic order`, high confidence, and documentation key `EFD014`, populate the shared finding contract, and span the paging operator invocation using one-based coordinates. Evidence SHALL identify the resolved paging operator, the proven EF origin, the observed inline query operators, and the absence of an ordering operator before the `Skip`. Impact language SHALL explain that results are non-deterministic across executions so pages can repeat or omit rows, without claiming a specific runtime cost. Remediation SHALL recommend adding a stable `OrderBy` (typically on a unique or tie-broken key) before `Skip`/`Take`, and SHALL acknowledge that an intentionally unordered sample can be suppressed with a recorded reason. The confidence-driven severity mapping and the universal test-project downgrade apply as for other rules.

#### Scenario: Complete structured finding
- **WHEN** EFD014 reports a paged query
- **THEN** console and JSON output contain every shared finding field with documentation key `EFD014`

#### Scenario: Exact diagnostic location
- **WHEN** EFD014 reports a paging operator
- **THEN** the finding range identifies the paging operator invocation using one-based coordinates

#### Scenario: Qualified remediation
- **WHEN** EFD014 reports a finding
- **THEN** remediation recommends a stable ordering before paging and names an intentionally unordered sample as a legitimate suppressible case

### Requirement: Support standard suppression and existing reporting behavior
EFD014 SHALL use standard Roslyn diagnostic suppression (pragma, editor configuration, and `SuppressMessage` where supported) and flow through the existing local-only CLI, deterministic ordering, de-duplication, and stable exit codes without a new command, schema version, network activity, or telemetry.

#### Scenario: Pragma suppression
- **WHEN** a matching paging operator is covered by `#pragma warning disable EFD014`
- **THEN** the analyzer result consumed by the CLI excludes that finding until warning restoration

#### Scenario: Editor configuration suppression
- **WHEN** applicable editor configuration sets `dotnet_diagnostic.EFD014.severity = none`
- **THEN** the analyzer result consumed by the CLI excludes EFD014 for that scope

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan produces EFD014 findings
- **THEN** both output formats contain the complete finding in deterministic order and retain the successful-scan-with-findings exit code and JSON schema version `1`

### Requirement: Validate EFD014 with focused and end-to-end tests
The change SHALL include analyzer fixtures covering positive cases (`Skip` only, `Skip().Take()`, `Skip` after `Where`, ordering applied after paging) and negative cases (ordering before paging, `Take`-only, in-memory sources, arbitrary `IQueryable`, stored-query locals, unrelated methods, and standard suppression), and SHALL include at least one CLI end-to-end test that verifies console and JSON output for an EFD014 fixture project.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** positive and negative EFD014 fixtures verify detection, the single-finding anchoring, ordering exclusion, source exclusions, and suppression

#### Scenario: CLI end-to-end fixture
- **WHEN** the CLI analyzes the EFD014 fixture project in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD014 findings with exact locations and JSON schema version `1`
