## MODIFIED Requirements

### Requirement: Bound-neutral composition does not change the outcome
EFD005 SHALL produce an identical outcome for two otherwise identical queries that differ only by a bound-neutral composition method. `AsSplitQuery`, `AsSingleQuery`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `IgnoreQueryFilters`, `IgnoreAutoIncludes`, `Include`, `ThenInclude`, `TagWith`, and `TagWithCallSite` are bound-neutral. They are part of the proven inline chain, and they MUST NOT establish, remove, or alter a recognized row bound. `IgnoreQueryFilters` can widen the rows a query returns, but EFD005 never treats a global query filter as a bound, so it does not change the outcome.

EFD005 remains deliberately inline-only: it does not follow an `IQueryable` value
stored in a local, field, property, parameter, return value, or helper. This proof
boundary applies independently of bound-neutral methods; adding or removing one of
those methods MUST NOT make an otherwise equivalent inline or stored query change
outcome.

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
- **WHEN** an otherwise equivalent query is composed through an intermediate `IQueryable` local
- **THEN** EFD005 does not follow that stored query in this version, regardless of whether a bound-neutral method appears

#### Scenario: Tracking and filter modifiers are bound-neutral
- **WHEN** an inline EF query adds `AsTracking`, `IgnoreQueryFilters`, or `IgnoreAutoIncludes`
- **THEN** EFD005 still proves the query's `DbSet` origin, reports it when it is unbounded, and does not report it when it is bounded
