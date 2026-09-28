# Spec Delta

## Purpose

Define a conservative semantic warning for EF Core query shapes whose distinct sibling collection includes may multiply rows in a single database result.

## ADDED Requirements

### Requirement: Semantically identify supported EF Core Include chains
EFD006 SHALL inspect resolved EF Core `EntityFrameworkQueryableExtensions.Include` calls only when the inline query chain can be traced through supported query composition to an EF Core `DbSet` or `DbContext.Set<TEntity>()`. Matching MUST use resolved method and type symbols rather than method-name text.

#### Scenario: Extension Include chain from DbSet
- **WHEN** an inline query rooted at `DbSet` uses resolved EF Core `Include` extension calls
- **THEN** EFD006 treats the chain as eligible for sibling-collection analysis

#### Scenario: Static Include syntax
- **WHEN** supported `Include` calls use valid static extension-method syntax over a proven EF source
- **THEN** EFD006 applies the same eligibility rules as extension syntax

#### Scenario: In-memory arbitrary or unrelated source
- **WHEN** a same-named call is unresolved, resolves outside EF Core, or its source is not proven to originate from a `DbSet`
- **THEN** EFD006 does not report

### Requirement: Report distinct sibling collection includes
EFD006 SHALL report a query chain when semantic analysis identifies at least two distinct root-level `Include` navigation expressions whose selected navigation types are collections. The first version SHALL emit one diagnostic for each query chain, located at the `Include` that introduces the second distinct sibling collection, and evidence SHALL name the distinct collection navigation paths.

#### Scenario: Two sibling collection navigations
- **WHEN** a proven EF query includes two distinct collection navigations from the query root
- **THEN** EFD006 reports the second collection `Include`

#### Scenario: Three sibling collection navigations
- **WHEN** a proven EF query includes three distinct root-level collection navigations
- **THEN** EFD006 emits one finding for that fluent query chain and identifies all observed sibling collection paths in evidence

#### Scenario: Reference plus collection navigation
- **WHEN** a query includes one reference navigation and one collection navigation
- **THEN** EFD006 does not report because no sibling collection product is established

#### Scenario: Duplicate collection path
- **WHEN** a query repeats the same root-level collection navigation path
- **THEN** EFD006 does not treat the duplicate path as a second distinct sibling collection

#### Scenario: Nested ThenInclude path
- **WHEN** a collection `Include` is followed only by collection or reference `ThenInclude` calls beneath that path
- **THEN** EFD006 does not report because the first version analyzes root-level sibling `Include` paths rather than nested branch cardinality

#### Scenario: Filtered collection Include
- **WHEN** two distinct sibling collection navigations use supported query operators inside their `Include` expressions
- **THEN** EFD006 still recognizes their root navigation properties as collection paths

### Requirement: Respect split-query and client boundaries
EFD006 SHALL NOT report a query chain containing a resolved EF Core `AsSplitQuery`, because the sibling collections are loaded using separate SQL queries. EFD006 SHALL NOT cross `AsEnumerable`, `AsAsyncEnumerable`, or another explicit client-evaluation boundary, and the first version SHALL NOT follow query values through locals, fields, properties, parameters, return values, helper methods, or general data flow.

#### Scenario: Explicit split query
- **WHEN** an otherwise eligible query chain applies resolved EF Core `AsSplitQuery` before or after its sibling collection includes
- **THEN** EFD006 does not report

#### Scenario: Explicit single query
- **WHEN** an eligible sibling-collection chain applies resolved EF Core `AsSingleQuery`
- **THEN** EFD006 remains reportable

#### Scenario: Stored query
- **WHEN** include calls and later query execution are separated through a local or member
- **THEN** EFD006 analyzes only each proven inline chain and does not infer composition across that boundary

#### Scenario: Explicit client boundary
- **WHEN** query composition crosses `AsEnumerable` or `AsAsyncEnumerable`
- **THEN** EFD006 does not infer a database include chain across that boundary

### Requirement: Emit a qualified actionable EFD006 finding
Each matching query chain SHALL produce exactly one EFD006 diagnostic with title `Multiple sibling collection includes may create a cartesian explosion`, severity `Warning`, and confidence `Medium`. Evidence SHALL identify the resolved EF Core calls, proven EF origin, and distinct sibling collection paths. The finding SHALL explain possible row multiplication and duplicated data without claiming measured impact.

#### Scenario: Complete structured finding
- **WHEN** EFD006 reports a query chain
- **THEN** console and JSON output contain every shared finding field with documentation key `EFD006`

#### Scenario: Exact diagnostic location
- **WHEN** EFD006 reports two or more sibling collection includes
- **THEN** the finding range identifies the complete `Include` invocation that introduces the second distinct collection path

#### Scenario: Qualified impact language
- **WHEN** EFD006 reports a finding
- **THEN** it states that a single SQL query may multiply rows and duplicate principal data as sibling collection cardinalities grow without claiming an observed slowdown

#### Scenario: Safe remediation guidance
- **WHEN** EFD006 reports a finding
- **THEN** remediation recommends reviewing generated SQL and result cardinality, considering `AsSplitQuery`, projections, or separate targeted queries, and accounting for consistency and round-trip trade-offs

### Requirement: Honor standard suppression and existing reporting contracts
EFD006 SHALL use standard Roslyn diagnostic suppression, including pragma, editor configuration, and `SuppressMessage` where supported. Findings SHALL flow through the existing local-only CLI, console and schema-versioned JSON renderers, deterministic ordering, de-duplication, and stable exit codes without a new command, schema version, network activity, telemetry, database execution, or proprietary suppression file.

#### Scenario: Standard suppression
- **WHEN** a matching query is suppressed through a supported Roslyn mechanism with a recorded justification
- **THEN** the analyzer result consumed by the CLI excludes that finding

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan produces EFD006 findings
- **THEN** both formats contain the complete actionable finding and retain JSON schema version `1` and the successful-scan-with-findings exit code

### Requirement: Verify EFD006 with focused and end-to-end tests
The change SHALL include at least ten positive and ten negative EFD006 analyzer fixtures. Coverage SHALL include extension and static syntax, direct and composed EF sources, two and three distinct sibling collections, filtered includes, reference navigations, duplicate paths, nested `ThenInclude`, split and single query modes, arbitrary queryables, stored queries, unrelated and unresolved methods, exact locations, multiple chains, and standard suppression. At least one end-to-end test SHALL invoke the CLI against a dedicated fixture and verify console and JSON output.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** at least ten positive and ten negative EFD006 cases verify the specified semantic matching, sibling classification, split-query exclusion, boundaries, and suppression

#### Scenario: CLI end-to-end fixture
- **WHEN** the CLI analyzes the EFD006 fixture project in console and JSON modes
- **THEN** both outputs contain only expected unsuppressed EFD006 findings with exact locations and JSON schema version `1`

#### Scenario: Full regression verification
- **WHEN** the solution is built and tested
- **THEN** EFD001 through EFD006 and all CLI tests pass without warnings, errors, or analysis-time network requirements
