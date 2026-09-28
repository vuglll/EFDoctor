# Spec Delta

## Purpose

Define local, high-confidence detection of a `StringComparison`-based string comparison inside an EF Core query predicate, which EF Core cannot translate to SQL and which throws at runtime.

## ADDED Requirements

### Requirement: Detect StringComparison inside a query predicate
EFD022 SHALL report a resolved `System.String` method call that accepts a `System.StringComparison` argument — `Equals`, `StartsWith`, `EndsWith`, `Contains`, `IndexOf` (instance), or `string.Equals`/`string.Compare` (static) — when the call occurs inside a lambda that is an argument to a resolved `System.Linq.Queryable` operator whose inline source traces to an EF Core `DbSet` or `DbContext.Set<TEntity>()`. Matching SHALL use resolved method and type symbols rather than name text. EFD022 SHALL anchor the finding on the offending string-method call.

#### Scenario: StringComparison in a Where predicate
- **WHEN** a proven EF query filters with `Where(x => x.Name.Equals(value, StringComparison.OrdinalIgnoreCase))`
- **THEN** EFD022 reports the `Equals` call

#### Scenario: StartsWith with StringComparison
- **WHEN** a proven EF query filters with `Where(x => x.Name.StartsWith(value, StringComparison.OrdinalIgnoreCase))`
- **THEN** EFD022 reports the `StartsWith` call

#### Scenario: Static string.Equals with StringComparison
- **WHEN** a proven EF query filters with `Where(x => string.Equals(x.Name, value, StringComparison.Ordinal))`
- **THEN** EFD022 reports the `string.Equals` call

#### Scenario: Ordinal string comparison without StringComparison is not reported
- **WHEN** a proven EF query filters with `Where(x => x.Name == value)` or `x.Name.StartsWith(value)` without a `StringComparison` argument
- **THEN** EFD022 does not report

#### Scenario: In-memory query is not reported
- **WHEN** the enclosing operator is an `System.Linq.Enumerable` operator over an in-memory sequence, or the source does not trace to a `DbSet`
- **THEN** EFD022 does not report

#### Scenario: StringComparison outside a query predicate is not reported
- **WHEN** a `StringComparison` string call occurs outside any EF Core query lambda (for example in ordinary client code)
- **THEN** EFD022 does not report

### Requirement: Produce actionable and qualified EFD022 findings
Each EFD022 finding SHALL have rule title `StringComparison is not translatable in a query predicate`, high confidence, and documentation key `EFD022`, populate the shared finding contract, and span the string-method call using one-based coordinates. Evidence SHALL identify the resolved string method, its `StringComparison` argument, and the proven EF origin of the enclosing query. Impact language SHALL explain that EF Core cannot translate `StringComparison` overloads to SQL and throws at runtime when the query executes, without claiming a specific runtime cost. Remediation SHALL recommend a translatable comparison — a plain `==`/`StartsWith`/`Contains` without `StringComparison`, or configuring a case-insensitive database collation — and SHALL acknowledge that a query intentionally evaluated on the client (for example after `AsEnumerable`) can be suppressed with a recorded reason.

#### Scenario: Complete structured finding
- **WHEN** EFD022 reports a call
- **THEN** console and JSON output contain every shared finding field with documentation key `EFD022`

#### Scenario: Exact diagnostic location
- **WHEN** EFD022 reports a string call
- **THEN** the finding range identifies the string-method call using one-based coordinates

#### Scenario: Qualified remediation
- **WHEN** EFD022 reports a finding
- **THEN** remediation recommends a translatable comparison or a database collation and names an intentional client-evaluated query as a legitimate suppressible case

### Requirement: Support standard suppression and existing reporting behavior
EFD022 SHALL use standard Roslyn diagnostic suppression (pragma, editor configuration, and `SuppressMessage` where supported) and flow through the existing local-only CLI, deterministic ordering, de-duplication, and stable exit codes without a new command, schema version, network activity, or telemetry.

#### Scenario: Pragma suppression
- **WHEN** a matching string call is covered by `#pragma warning disable EFD022`
- **THEN** the analyzer result consumed by the CLI excludes that finding until warning restoration

#### Scenario: Editor configuration suppression
- **WHEN** applicable editor configuration sets `dotnet_diagnostic.EFD022.severity = none`
- **THEN** the analyzer result consumed by the CLI excludes EFD022 for that scope

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan produces EFD022 findings
- **THEN** both output formats contain the complete finding in deterministic order and retain the successful-scan-with-findings exit code and JSON schema version `1`

### Requirement: Validate EFD022 with focused and end-to-end tests
The change SHALL include analyzer fixtures covering positive cases (`Equals`/`StartsWith`/`EndsWith`/`Contains` with `StringComparison`, static `string.Equals`, composed chains) and negative cases (no `StringComparison` argument, in-memory `Enumerable` queries, string calls outside a query predicate, unrelated methods, and standard suppression), and SHALL include at least one CLI end-to-end test that verifies console and JSON output for an EFD022 fixture project.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** positive and negative EFD022 fixtures verify detection, the in-memory and non-predicate exclusions, type resolution, and suppression

#### Scenario: CLI end-to-end fixture
- **WHEN** the CLI analyzes the EFD022 fixture project in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD022 findings with exact locations and JSON schema version `1`
