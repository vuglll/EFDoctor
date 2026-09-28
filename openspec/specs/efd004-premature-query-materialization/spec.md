# EFD004 Premature Query Materialization Specification

## Purpose

Define high-confidence detection of EF Core queries that are buffered before directly composed filtering, projection, ordering, or paging that can remain database-side.

## Requirements

### Requirement: Semantically identify supported EF Core materialization
EFD004 SHALL recognize synchronous `System.Linq.Enumerable.ToList` and `ToArray` invocations only when the analyzed source expression can be traced through a supported query chain to an EF Core `DbSet`. It SHALL recognize asynchronous `ToListAsync` and `ToArrayAsync` only when they resolve to EF Core `EntityFrameworkQueryableExtensions` methods and their source can likewise be traced to a `DbSet`. Matching MUST use resolved method and type symbols rather than method-name text.

#### Scenario: Direct DbSet synchronous materialization
- **WHEN** `ToList` or `ToArray` is invoked directly on an EF Core `DbSet` and participates in a supported post-materialization composition
- **THEN** EFD004 treats it as eligible EF query materialization

#### Scenario: Composed EF query materialization
- **WHEN** a supported materializer follows a semantically resolved query chain whose source is an EF Core `DbSet`
- **THEN** EFD004 treats it as eligible even when server-side operators already precede materialization

#### Scenario: Asynchronous EF materialization
- **WHEN** an awaited EF Core `ToListAsync` or `ToArrayAsync` invocation has a source traced to a `DbSet` and participates in a supported post-materialization composition
- **THEN** EFD004 treats it as eligible EF query materialization

#### Scenario: In-memory collection
- **WHEN** `ToList` or `ToArray` operates on an array, list, or other source not proven to originate from a `DbSet`
- **THEN** EFD004 does not report

#### Scenario: Arbitrary IQueryable
- **WHEN** a materializer operates on an `IQueryable<T>` whose analyzed source expression cannot be traced to an EF Core `DbSet`
- **THEN** EFD004 does not report

#### Scenario: Unrelated or unresolved materializer
- **WHEN** a same-named method resolves outside the supported LINQ or EF Core APIs, or does not resolve successfully
- **THEN** EFD004 does not report

### Requirement: Detect directly composed SQL-capable work after materialization
EFD004 SHALL report when an eligible materialization result is consumed immediately, within the same fluent expression, by `System.Linq.Enumerable.Where`, `Select`, `OrderBy`, `OrderByDescending`, `Skip`, or `Take`, and the downstream arguments meet the rule's conservative SQL-capable shape requirements. Parentheses, implicit conversions, and an `await` around asynchronous materialization SHALL NOT prevent a match.

#### Scenario: Filtering after materialization
- **WHEN** an eligible materialization is immediately followed by a supported `Where` predicate
- **THEN** EFD004 reports the materialization

#### Scenario: Projection after materialization
- **WHEN** an eligible materialization is immediately followed by a supported `Select` projection
- **THEN** EFD004 reports the materialization

#### Scenario: Ordering after materialization
- **WHEN** an eligible materialization is immediately followed by a supported ordering operator
- **THEN** EFD004 reports the materialization

#### Scenario: Paging after materialization
- **WHEN** an eligible materialization is immediately followed by a supported `Skip` or `Take` count
- **THEN** EFD004 reports the materialization

#### Scenario: Async materialization followed by filtering
- **WHEN** an awaited eligible asynchronous materialization is parenthesized and immediately followed by a supported downstream operator
- **THEN** EFD004 reports the asynchronous materialization invocation

#### Scenario: Multiple downstream operators
- **WHEN** one eligible materialization begins a fluent sequence containing multiple supported downstream operators
- **THEN** EFD004 emits one diagnostic for that materialization and identifies the immediate supported operator as evidence

### Requirement: Conservatively classify downstream argument shapes
A supported `Where` predicate SHALL be limited to direct entity member access, constants, parameters or captured locals, null checks, built-in unary operations, built-in comparisons, and built-in boolean combinations of those forms. A supported `Select` SHALL be limited to direct member access or an anonymous-object projection composed from supported members and scalar expressions. A supported ordering key SHALL be a direct member-access path. A supported `Skip` or `Take` count SHALL be an integral constant, parameter, or captured local. User-defined operators, dynamic operations, assignments, object mutation, and arbitrary method or property invocations SHALL make the downstream operation ineligible.

#### Scenario: Simple scalar predicate
- **WHEN** `Where` compares an entity scalar member with a constant, parameter, or captured local using built-in operators
- **THEN** the predicate is eligible for EFD004

#### Scenario: Boolean predicate composition
- **WHEN** a `Where` predicate combines supported comparisons with built-in `&&`, `||`, or `!`
- **THEN** the predicate remains eligible for EFD004

#### Scenario: Direct property projection
- **WHEN** `Select` returns an entity member or an anonymous object composed only from supported entity members and scalar expressions
- **THEN** the projection is eligible for EFD004

#### Scenario: Direct property ordering
- **WHEN** an ordering operator selects a direct entity member-access path
- **THEN** the ordering is eligible for EFD004

#### Scenario: Stable paging value
- **WHEN** `Skip` or `Take` receives an integral literal, parameter, or captured local
- **THEN** the paging operation is eligible for EFD004

#### Scenario: Custom method in predicate or projection
- **WHEN** the immediate downstream lambda invokes an arbitrary application method or accesses an unsupported computed member
- **THEN** EFD004 does not report because server translation is not established conservatively

#### Scenario: User-defined operator
- **WHEN** a downstream expression uses a user-defined operator
- **THEN** EFD004 does not report

### Requirement: Preserve intentional client-evaluation boundaries and expression-local scope
EFD004 SHALL NOT report when `AsEnumerable` or `AsAsyncEnumerable` establishes an explicit client-evaluation boundary before materialization. EFD004 SHALL analyze the materializer's containing fluent expression. It MAY trace the materializer's query source through a query local whose value is statically determined, as defined by `query-chain-local-tracking`. It SHALL NOT follow a materialized result through a local, field, property, return value, argument, helper method, or general data flow.

#### Scenario: Explicit AsEnumerable boundary
- **WHEN** an EF query explicitly crosses `AsEnumerable` before materialization and later applies a LINQ operator
- **THEN** EFD004 does not report

#### Scenario: Explicit AsAsyncEnumerable boundary
- **WHEN** an EF query explicitly crosses `AsAsyncEnumerable` before client-side asynchronous composition
- **THEN** EFD004 does not report

#### Scenario: Materialized local used later
- **WHEN** a supported materialization is assigned to a local and a later statement filters, projects, orders, or pages that local
- **THEN** EFD004 does not report in the first version

#### Scenario: Materialization is terminal
- **WHEN** an eligible EF query is materialized and no supported operator is composed afterward
- **THEN** EFD004 does not report

#### Scenario: Filtering occurs before materialization
- **WHEN** a supported filter, projection, ordering, or paging operator is part of the EF query before its terminal materialization and no supported operator follows materialization
- **THEN** EFD004 does not report

#### Scenario: Unsupported immediate operator precedes a supported one
- **WHEN** the immediate operation after materialization is unsupported or ineligible even though a later operation in the same expression is supported
- **THEN** EFD004 does not skip over the immediate operation to report the materialization

#### Scenario: Comments strings and inactive code
- **WHEN** relevant method names occur only in comments, strings, or inactive preprocessor code
- **THEN** EFD004 emits no diagnostic for that text

#### Scenario: Query source stored in a local
- **WHEN** an EF query is stored in a local whose value is statically determined, and a later statement materializes that local and immediately filters, projects, orders, or pages the result
- **THEN** EFD004 reports as it would for the equivalent inline query

### Requirement: Emit one actionable EFD004 finding per materialization
Each matching materialization SHALL produce exactly one EFD004 diagnostic with title `Query is materialized before SQL-capable composition`, severity `Warning`, and confidence `High`. The source span SHALL identify the complete materializer invocation, and the shared finding fields SHALL include the resolved materializer, immediate downstream operator, proven EF query origin, qualified impact, suggested remediation, and documentation key `EFD004`.

#### Scenario: Exact diagnostic location
- **WHEN** EFD004 reports a synchronous or asynchronous materializer
- **THEN** console and JSON ranges identify the exact one-based start and end coordinates of the materializer invocation without including the downstream operator

#### Scenario: Multiple materializations
- **WHEN** source contains multiple distinct matching materializations
- **THEN** EFD004 emits one finding for each without aggregate or duplicate diagnostics

#### Scenario: Evidence identifies the expression boundary
- **WHEN** EFD004 reports a finding
- **THEN** its evidence identifies the resolved materializer, the EF query origin, and the immediate supported operation that will execute over buffered client data

### Requirement: Explain impact and remediation without overstating certainty
Every EFD004 finding SHALL explain that early materialization can transfer and buffer more rows or columns than needed and can prevent later supported work from being translated into SQL. It MUST NOT claim that every occurrence is incorrect, that the result set is large, or that a measured performance improvement is guaranteed. Remediation SHALL recommend moving supported composition before materialization while preserving semantics and verifying generated SQL.

#### Scenario: Filtering or paging impact
- **WHEN** EFD004 reports `Where`, `Skip`, or `Take` after materialization
- **THEN** the finding explains the risk of transferring or buffering rows that could have been filtered or limited by the database

#### Scenario: Projection impact
- **WHEN** EFD004 reports `Select` after materialization
- **THEN** the finding explains the risk of retrieving columns or tracked entities that the final projection may not require

#### Scenario: Ordering impact
- **WHEN** EFD004 reports an ordering operation after materialization
- **THEN** the finding explains that ordering occurs over buffered client data instead of being composed into the database query

#### Scenario: Legitimate exception guidance
- **WHEN** EFD004 reports a finding
- **THEN** remediation acknowledges small bounded result sets, intentional client-only logic, repeated enumeration or reuse, provider translation limitations, and semantic or tracking requirements as reasons to review rather than blindly rewrite

### Requirement: Honor standard suppression and existing CLI contracts
EFD004 SHALL use standard Roslyn diagnostic suppression, including pragma, editor configuration, and `SuppressMessage` where supported. Findings SHALL flow through the existing local-only CLI, console and schema-versioned JSON renderers, deterministic ordering, de-duplication, and stable exit-code behavior without a new command, schema version, network activity, telemetry, or proprietary suppression file.

#### Scenario: Pragma suppression
- **WHEN** a matching materializer is covered by `#pragma warning disable EFD004`
- **THEN** the analyzer result consumed by the CLI excludes that diagnostic until warning restoration

#### Scenario: Editor configuration suppression
- **WHEN** applicable editor configuration sets `dotnet_diagnostic.EFD004.severity = none`
- **THEN** the analyzer result consumed by the CLI excludes EFD004 for that scope

#### Scenario: SuppressMessage with justification
- **WHEN** a supported `SuppressMessage` targets EFD004 and records why client-side composition is intentional
- **THEN** the analyzer result consumed by the CLI excludes the targeted diagnostic

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan produces EFD004 findings
- **THEN** both output formats contain the complete actionable finding in deterministic order and retain their existing successful-scan-with-findings exit code and JSON schema version

### Requirement: Verify EFD004 with focused and end-to-end tests
The change SHALL include at least ten positive and ten negative EFD004 analyzer fixtures. Coverage SHALL include every supported materializer category and downstream operator category, synchronous and asynchronous expressions, composed EF sources, argument-shape boundaries, explicit client boundaries, unrelated and unresolved methods, expression-local limitations, multiple findings, exact locations, and standard suppression. At least one end-to-end test SHALL invoke the CLI against a fixture project and verify both console and JSON output.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** at least ten positive and ten negative EFD004 cases verify the specified detection and exclusion behavior

#### Scenario: CLI end-to-end fixture
- **WHEN** the CLI analyzes the EFD004 fixture project in console and JSON modes
- **THEN** both outputs contain the expected unsuppressed EFD004 findings, exclude suppressed cases, preserve exact locations, and retain JSON schema version `1`

#### Scenario: Full regression verification
- **WHEN** the solution is built and tested from a clean checkout
- **THEN** EFD001 through EFD004 and all CLI tests pass without warnings, errors, or analysis-time network requirements
