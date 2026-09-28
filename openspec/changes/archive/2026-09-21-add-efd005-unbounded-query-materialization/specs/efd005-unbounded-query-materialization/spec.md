# EFD005 Unbounded Query Materialization Specification

## Purpose

Define a qualified, semantic warning for EF Core list materialization whose inline database query has no recognized query-side row bound.

## ADDED Requirements

### Requirement: Semantically identify supported EF Core list materialization
EFD005 SHALL recognize synchronous `System.Linq.Enumerable.ToList` and asynchronous EF Core `EntityFrameworkQueryableExtensions.ToListAsync` invocations only when their inline source expression can be traced through supported query composition to an EF Core `DbSet` or `DbContext.Set<TEntity>()`. Matching MUST use resolved method and type symbols rather than method-name text.

#### Scenario: Direct DbSet ToList
- **WHEN** `ToList` is invoked directly on an EF Core `DbSet`
- **THEN** EFD005 treats it as eligible database-query materialization

#### Scenario: Composed EF query ToListAsync
- **WHEN** `ToListAsync` follows a semantically resolved inline EF query chain originating from `DbSet`
- **THEN** EFD005 treats it as eligible whether it is awaited inline or its task is consumed elsewhere

#### Scenario: Static materializer syntax
- **WHEN** a supported materializer is invoked with valid static extension-method syntax over a proven EF source
- **THEN** EFD005 applies the same eligibility rules as extension syntax

#### Scenario: In-memory or arbitrary query source
- **WHEN** `ToList` operates on an in-memory sequence or an `IQueryable<T>` whose inline source is not proven to originate from `DbSet`
- **THEN** EFD005 does not report

#### Scenario: Unrelated or unresolved materializer
- **WHEN** a same-named method resolves outside the supported LINQ or EF Core APIs, or does not resolve successfully
- **THEN** EFD005 does not report

### Requirement: Report absence of a recognized query-side row bound
EFD005 SHALL report an eligible materialization when no resolved `System.Linq.Queryable.Take` occurs in its inline EF query chain before materialization. Filtering, projection, ordering, `Distinct`, `Skip`, `Include`, and tracking modifiers MUST NOT be treated as row bounds by themselves.

#### Scenario: Direct unbounded materialization
- **WHEN** a proven `DbSet` source is materialized with `ToList` or `ToListAsync` without query-side `Take`
- **THEN** EFD005 reports the materializer

#### Scenario: Filtered query without Take
- **WHEN** an inline EF query contains `Where` but no query-side `Take`
- **THEN** EFD005 reports because the filter does not establish a maximum row count

#### Scenario: Projection ordering or Skip without Take
- **WHEN** an inline EF query contains projection, ordering, or `Skip` but no query-side `Take`
- **THEN** EFD005 reports because those operations do not establish a maximum row count

#### Scenario: Include or tracking modifier without Take
- **WHEN** an inline EF query contains resolved EF `Include`, `AsNoTracking`, or related composition but no query-side `Take`
- **THEN** EFD005 reports the materializer

#### Scenario: Multiple unbounded materializations
- **WHEN** source contains multiple distinct eligible materializations without recognized bounds
- **THEN** EFD005 emits one finding for each materialization

### Requirement: Recognize explicit query-side Take bounds
EFD005 SHALL treat a resolved `System.Linq.Queryable.Take` anywhere in the proven inline EF query chain before materialization as an explicit row bound. The bound MAY be a constant, parameter, or other successfully bound count expression because EFD005 establishes the presence of a limit, not its business suitability or magnitude.

#### Scenario: Constant Take bound
- **WHEN** an eligible EF query applies `Take(100)` before list materialization
- **THEN** EFD005 does not report

#### Scenario: Parameterized Take bound
- **WHEN** an eligible EF query applies `Take(limit)` before list materialization
- **THEN** EFD005 does not report even though the runtime value is unknown

#### Scenario: Take before later query composition
- **WHEN** a resolved query-side `Take` is followed by additional supported `Queryable` or EF composition before materialization
- **THEN** EFD005 still treats the materialization as explicitly bounded

#### Scenario: Skip without Take
- **WHEN** an eligible EF query uses `Skip` without a later or earlier query-side `Take`
- **THEN** EFD005 reports

#### Scenario: Unrelated Take method
- **WHEN** a same-named `Take` resolves outside `System.Linq.Queryable`
- **THEN** it does not establish an EFD005 bound

### Requirement: Preserve explicit client boundaries and expression-local scope
EFD005 SHALL NOT cross `AsEnumerable`, `AsAsyncEnumerable`, or another client-evaluation boundary to infer database materialization. The first version SHALL trace only the materializer's containing operation tree and SHALL NOT follow query values through locals, fields, properties, parameters, return values, helper methods, or general data flow.

#### Scenario: Explicit AsEnumerable boundary
- **WHEN** an EF query crosses `AsEnumerable` before `ToList`
- **THEN** EFD005 does not report because the analyzed materializer is explicitly client-side

#### Scenario: Arbitrary IQueryable parameter
- **WHEN** `ToList` or `ToListAsync` receives an `IQueryable<T>` parameter without an inline proven `DbSet` origin
- **THEN** EFD005 does not report

#### Scenario: Query stored before materialization
- **WHEN** an EF query is assigned to a local and materialized in a later statement
- **THEN** EFD005 does not report in the first version

#### Scenario: Comments strings and inactive code
- **WHEN** relevant method names occur only in comments, strings, or inactive preprocessor code
- **THEN** EFD005 emits no diagnostic for that text

### Requirement: Avoid duplicate EFD004 findings at one materializer
EFD005 SHALL NOT report a materializer when the same invocation is eligible for EFD004 premature query materialization. EFD004 SHALL remain the more specific explanation when supported filtering, projection, ordering, or paging is immediately composed over the buffered result.

#### Scenario: Immediate SQL-capable filtering after ToList
- **WHEN** an unbounded materializer is immediately consumed by a supported EFD004 `Where` expression
- **THEN** EFD004 may report and EFD005 does not report at that materializer

#### Scenario: Terminal unbounded ToList
- **WHEN** an eligible unbounded materializer has no EFD004-eligible immediate client composition
- **THEN** EFD005 reports independently

#### Scenario: Unsupported client composition
- **WHEN** an eligible unbounded materializer is followed immediately by client composition that EFD004 cannot classify as SQL-capable
- **THEN** EFD005 may report because no duplicate EFD004 finding exists

### Requirement: Emit a qualified actionable EFD005 finding
Each matching invocation SHALL produce exactly one EFD005 diagnostic with title `Query materialization has no recognized row bound`, severity `Warning`, and confidence `Medium`. The source span SHALL identify the complete materializer invocation. Evidence SHALL identify the resolved materializer, proven EF origin, observed inline query operations, and absence of query-side `Take`.

#### Scenario: Complete structured finding
- **WHEN** EFD005 reports a materialization
- **THEN** console and JSON output contain every shared finding field with documentation key `EFD005`

#### Scenario: Exact diagnostic location
- **WHEN** EFD005 reports `ToList` or `ToListAsync`
- **THEN** the finding range identifies the complete materializer invocation using one-based coordinates

#### Scenario: Qualified impact language
- **WHEN** EFD005 reports a finding
- **THEN** it explains that an unbounded query may transfer and buffer more rows than expected and increase database work, memory use, or latency without claiming the result is large or incorrect

#### Scenario: Semantics-preserving remediation
- **WHEN** EFD005 reports a finding
- **THEN** remediation recommends reviewing filtering and paging, streaming or chunking, or a purpose-built aggregate or terminal operation while warning against adding an arbitrary limit that changes required results

#### Scenario: Legitimate full-result guidance
- **WHEN** EFD005 reports a finding
- **THEN** remediation acknowledges exports, batch processing, migrations, cache warm-up, and known-small lookup tables as possible intentional cases

### Requirement: Honor standard suppression and existing CLI contracts
EFD005 SHALL use standard Roslyn diagnostic suppression, including pragma, editor configuration, and `SuppressMessage` where supported. Findings SHALL flow through the existing local-only CLI, console and schema-versioned JSON renderers, deterministic ordering, de-duplication, and stable exit codes without a new command, schema version, network activity, telemetry, or proprietary suppression file.

#### Scenario: Pragma suppression
- **WHEN** a matching materializer is covered by `#pragma warning disable EFD005`
- **THEN** the analyzer result consumed by the CLI excludes that finding until warning restoration

#### Scenario: Editor configuration suppression
- **WHEN** applicable editor configuration sets `dotnet_diagnostic.EFD005.severity = none`
- **THEN** the analyzer result consumed by the CLI excludes EFD005 for that scope

#### Scenario: SuppressMessage with justification
- **WHEN** a supported `SuppressMessage` targets EFD005 and records why full materialization is intentional
- **THEN** the analyzer result consumed by the CLI excludes the targeted finding

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan produces EFD005 findings
- **THEN** both output formats contain the complete actionable finding in deterministic order and retain the successful-scan-with-findings exit code and JSON schema version `1`

### Requirement: Verify EFD005 with focused and end-to-end tests
The change SHALL include at least ten positive and ten negative EFD005 analyzer fixtures. Coverage SHALL include synchronous and asynchronous materialization, direct and composed EF sources, static and extension syntax, recognized `Take` bounds, non-bounding query operators, explicit client boundaries, arbitrary queryables, unrelated and unresolved methods, EFD004 overlap, multiple findings, exact locations, and standard suppression. At least one end-to-end test SHALL invoke the CLI against a dedicated fixture and verify console and JSON output.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** at least ten positive and ten negative EFD005 cases verify the specified detection, bounding, overlap, and exclusion behavior

#### Scenario: CLI end-to-end fixture
- **WHEN** the CLI analyzes the EFD005 fixture project in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD005 findings, preserve exact locations, and retain JSON schema version `1`

#### Scenario: Full regression verification
- **WHEN** the solution is built and tested from a clean checkout
- **THEN** EFD001 through EFD005 and all CLI tests pass without warnings, errors, or analysis-time network requirements
