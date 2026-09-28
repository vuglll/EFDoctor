# Spec Delta

## Purpose

Define local, medium-confidence detection of a synchronous EF Core database call made inside an async context that has a direct EF async counterpart, so the call can be awaited instead of blocking a thread.

## ADDED Requirements

### Requirement: Detect a synchronous EF Core database call in an async context
EFD010 SHALL report a resolved synchronous EF Core database call that occurs inside an `async` method or `async` lambda and that has a direct EF async counterpart. The following are in scope: a materializing or aggregating LINQ terminal — `ToList`, `ToArray`, `ToDictionary`, `ToHashSet`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `Count`, `LongCount`, `Any`, `All`, `Min`, `Max`, `Sum`, `Average`, `Contains`, `ElementAt` — whose source traces through supported composition to an EF Core `DbSet`; `SaveChanges` on a `DbContext`; and `Find` on a `DbSet`. Matching SHALL use resolved method and type symbols rather than name text. EFD010 SHALL anchor the finding on the synchronous call.

#### Scenario: Synchronous terminal in an async method
- **WHEN** an `async` method calls `context.Orders.Where(...).ToList()`
- **THEN** EFD010 reports the `ToList` call and names `ToListAsync` as the replacement

#### Scenario: SaveChanges in an async method
- **WHEN** an `async` method calls `context.SaveChanges()`
- **THEN** EFD010 reports the call and names `SaveChangesAsync` as the replacement

#### Scenario: Find in an async method
- **WHEN** an `async` method calls `context.Orders.Find(id)`
- **THEN** EFD010 reports the call and names `FindAsync` as the replacement

#### Scenario: Synchronous call in an async lambda
- **WHEN** a synchronous EF terminal is called inside an `async` lambda
- **THEN** EFD010 reports the call

#### Scenario: Synchronous method is not reported
- **WHEN** the enclosing method or lambda is not `async`
- **THEN** EFD010 does not report, because awaiting is not available without restructuring

#### Scenario: Already asynchronous call is not reported
- **WHEN** the code already calls the `*Async` counterpart (for example `ToListAsync`)
- **THEN** EFD010 does not report

#### Scenario: In-memory or non-EF source is not reported
- **WHEN** a terminal operates on an in-memory sequence or an `IQueryable<T>` not proven to originate from a `DbSet`
- **THEN** EFD010 does not report

### Requirement: Produce actionable and qualified EFD010 findings
Each EFD010 finding SHALL have rule title `Synchronous database call in an async method`, medium confidence, and documentation key `EFD010`, populate the shared finding contract, and span the synchronous call using one-based coordinates. Evidence SHALL identify the resolved synchronous method, the proven EF origin (or `DbContext`/`DbSet` receiver), and the suggested `*Async` counterpart. Impact language SHALL explain that a synchronous database call blocks the calling thread for the round trip and reduces async scalability, without claiming a specific runtime cost. Remediation SHALL recommend awaiting the EF async counterpart, and SHALL acknowledge that a synchronous call is legitimate outside a hot request path and can be suppressed with a recorded reason.

#### Scenario: Complete structured finding
- **WHEN** EFD010 reports a call
- **THEN** console and JSON output contain every shared finding field with documentation key `EFD010`

#### Scenario: Exact diagnostic location
- **WHEN** EFD010 reports a synchronous call
- **THEN** the finding range identifies the call using one-based coordinates

#### Scenario: Qualified remediation
- **WHEN** EFD010 reports a finding
- **THEN** remediation names the specific `*Async` counterpart to await and names non-hot-path usage as a legitimate suppressible case

### Requirement: Support standard suppression and existing reporting behavior
EFD010 SHALL use standard Roslyn diagnostic suppression (pragma, editor configuration, and `SuppressMessage` where supported) and flow through the existing local-only CLI, deterministic ordering, de-duplication, and stable exit codes without a new command, schema version, network activity, or telemetry.

#### Scenario: Pragma suppression
- **WHEN** a matching call is covered by `#pragma warning disable EFD010`
- **THEN** the analyzer result consumed by the CLI excludes that finding until warning restoration

#### Scenario: Editor configuration suppression
- **WHEN** applicable editor configuration sets `dotnet_diagnostic.EFD010.severity = none`
- **THEN** the analyzer result consumed by the CLI excludes EFD010 for that scope

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan produces EFD010 findings
- **THEN** both output formats contain the complete finding in deterministic order and retain the successful-scan-with-findings exit code and JSON schema version `1`

### Requirement: Validate EFD010 with focused and end-to-end tests
The change SHALL include analyzer fixtures covering positive cases (a synchronous terminal, `SaveChanges`, and `Find` in an async method, and a terminal in an async lambda) and negative cases (the same calls in a non-async method, an already-async call, an in-memory sequence, an arbitrary `IQueryable`, and standard suppression), and SHALL include at least one CLI end-to-end test that verifies console and JSON output for an EFD010 fixture project.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** positive and negative EFD010 fixtures verify the async-context requirement, the async-counterpart and source exclusions, and suppression

#### Scenario: CLI end-to-end fixture
- **WHEN** the CLI analyzes the EFD010 fixture project in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD010 findings with exact locations and JSON schema version `1`
