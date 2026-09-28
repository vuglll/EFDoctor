## MODIFIED Requirements

### Requirement: Bound-neutral composition does not change the outcome
EFD005 SHALL produce an identical outcome for two otherwise identical queries that differ only by a bound-neutral composition method. `AsSplitQuery`, `AsSingleQuery`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `IgnoreQueryFilters`, `IgnoreAutoIncludes`, `Include`, `ThenInclude`, `TagWith`, and `TagWithCallSite` are bound-neutral. They are part of the proven inline chain, and they MUST NOT establish, remove, or alter a recognized row bound. `IgnoreQueryFilters` can widen the rows a query returns, but EFD005 never treats a global query filter as a bound, so it does not change the outcome.

EFD005 follows an `IQueryable` value stored in a local only when the local's value is statically determined, as defined by `query-chain-local-tracking`. It does not follow one stored in a field, property, parameter, return value, or helper. A query composed through a followable local SHALL have the same outcome as the equivalent inline query. Adding or removing a bound-neutral method MUST NOT make an otherwise equivalent inline or stored query change outcome.

#### Scenario: AsSplitQuery parity
- **WHEN** two identical bounded queries differ only by a trailing `AsSplitQuery`
- **THEN** EFD005 reports neither

#### Scenario: AsSplitQuery parity on unbounded queries
- **WHEN** two identical unbounded queries differ only by a trailing `AsSplitQuery`
- **THEN** EFD005 reports both

#### Scenario: Include and tracking modifiers are bound-neutral
- **WHEN** an inline EF query adds `Include`, `ThenInclude`, `AsNoTracking`, or `TagWith` to a bounded query
- **THEN** EFD005 still recognizes the underlying bound and does not report

#### Scenario: Stored query proof boundary is token-independent
- **WHEN** an otherwise equivalent query is composed through an intermediate `IQueryable` local whose value is statically determined, with or without a bound-neutral method
- **THEN** EFD005 reports it exactly when it reports the equivalent inline query

#### Scenario: Tracking and filter modifiers are bound-neutral
- **WHEN** an inline EF query adds `AsTracking`, `IgnoreQueryFilters`, or `IgnoreAutoIncludes`
- **THEN** EFD005 still proves the query's `DbSet` origin, reports it when it is unbounded, and does not report it when it is bounded

### Requirement: Preserve explicit client boundaries and expression-local scope
EFD005 SHALL NOT cross `AsEnumerable`, `AsAsyncEnumerable`, or another client-evaluation boundary to infer database materialization. EFD005 SHALL trace the materializer's containing operation tree, and through query locals whose value is statically determined, as defined by `query-chain-local-tracking`. It SHALL NOT follow query values through other locals, fields, properties, parameters, return values, helper methods, or general data flow.

#### Scenario: Explicit AsEnumerable boundary
- **WHEN** an EF query crosses `AsEnumerable` before `ToList`
- **THEN** EFD005 does not report because the analyzed materializer is explicitly client-side

#### Scenario: Arbitrary IQueryable parameter
- **WHEN** `ToList` or `ToListAsync` receives an `IQueryable<T>` parameter without an inline proven `DbSet` origin
- **THEN** EFD005 does not report

#### Scenario: Query stored before materialization
- **WHEN** an unbounded EF query is assigned to a local whose value is statically determined, and materialized in a later statement
- **THEN** EFD005 reports it as it would the equivalent inline query, and its evidence names the local

#### Scenario: Conditionally composed stored query
- **WHEN** an EF query local is reassigned inside an `if` statement before it is materialized
- **THEN** EFD005 does not report, because the composition at the materializer is not statically determined

#### Scenario: Comments strings and inactive code
- **WHEN** relevant method names occur only in comments, strings, or inactive preprocessor code
- **THEN** EFD005 emits no diagnostic for that text
