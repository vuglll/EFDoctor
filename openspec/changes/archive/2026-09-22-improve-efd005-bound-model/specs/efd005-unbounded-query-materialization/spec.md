# Spec Delta

## Purpose

Redefine EFD005 so a row bound may be established by the query predicate, not only by `Queryable.Take`; guarantee that bound-neutral composition never changes the outcome; downgrade findings in test projects; replace the remediation guidance; and replace flat confidence with a blast-radius gradient.

## MODIFIED Requirements

### Requirement: Report absence of a recognized query-side row bound
EFD005 SHALL report an eligible materialization only when its proven inline EF query chain establishes no strong row bound. A strong row bound is a resolved `System.Linq.Queryable.Take`, a local-collection membership predicate, or a key-equality predicate as defined in "Recognize predicate-derived row bounds". Filtering that does not bound rows, projection, ordering, `Distinct`, `Skip`, `Include`, and tracking modifiers MUST NOT be treated as row bounds by themselves. A medium- or weak-strength bound SHALL downgrade confidence rather than suppress the finding.

#### Scenario: Direct unbounded materialization
- **WHEN** a proven `DbSet` source is materialized with `ToList` or `ToListAsync` with no `Take` and no bounding predicate
- **THEN** EFD005 reports the materializer

#### Scenario: Filtered query without Take
- **WHEN** an inline EF query contains a `Where` whose predicate establishes no recognized bound and no query-side `Take`
- **THEN** EFD005 reports because the filter does not establish a maximum row count

#### Scenario: Projection ordering or Skip without Take
- **WHEN** an inline EF query contains projection, ordering, or `Skip` but no recognized strong bound
- **THEN** EFD005 reports because those operations do not establish a maximum row count

#### Scenario: Include or tracking modifier without Take
- **WHEN** an inline EF query contains resolved EF `Include`, `AsNoTracking`, or related composition but no recognized strong bound
- **THEN** EFD005 reports the materializer because those modifiers are bound-neutral

#### Scenario: Strong predicate bound present
- **WHEN** an inline EF query establishes a strong row bound through a bounding predicate
- **THEN** EFD005 does not report

#### Scenario: Multiple unbounded materializations
- **WHEN** source contains multiple distinct eligible materializations without recognized strong bounds
- **THEN** EFD005 emits one finding for each materialization

### Requirement: Emit a qualified actionable EFD005 finding
Each matching invocation SHALL produce exactly one EFD005 diagnostic with title `Query materialization has no recognized row bound`. Confidence SHALL be assigned per "Assign EFD005 confidence by blast radius" and severity SHALL follow confidence-driven severity mapping. The source span SHALL identify the complete materializer invocation. Evidence SHALL identify the resolved materializer, proven EF origin, observed inline query operations, and the recognized bound kind or its absence.

#### Scenario: Complete structured finding
- **WHEN** EFD005 reports a materialization
- **THEN** console and JSON output contain every shared finding field with documentation key `EFD005`

#### Scenario: Exact diagnostic location
- **WHEN** EFD005 reports `ToList` or `ToListAsync`
- **THEN** the finding range identifies the complete materializer invocation using one-based coordinates

#### Scenario: Qualified impact language
- **WHEN** EFD005 reports a finding
- **THEN** it explains that a query without a recognized bound may transfer and buffer more rows than expected and increase database work, memory use, or latency without claiming the result is large or incorrect

#### Scenario: Semantics-preserving remediation
- **WHEN** EFD005 reports a finding
- **THEN** remediation leads with the question of whether the result set can grow without bound and whether every row is needed, recommends server-side paging with stable ordering, chunked processing, or a purpose-built aggregate, does not present `Take` as the default action, and does not recommend adding an arbitrary limit that changes required results

#### Scenario: Legitimate full-result guidance
- **WHEN** EFD005 reports a finding
- **THEN** remediation names hydrating children for a known set of parents, exports, batch processing, migrations, cache warm-up, and known-small lookup tables as legitimate full-materialization cases that may be suppressed with a recorded reason

## ADDED Requirements

### Requirement: Recognize predicate-derived row bounds
EFD005 SHALL recognize row bounds established by the predicate of a resolved `Queryable.Where` in the proven inline chain, in addition to `Queryable.Take`. A local-collection membership predicate of the form `where entity => collection.Contains(entity.Property)` or `where entity => collection.Any(...)`, whose receiver is a local, parameter, or field collection and whose argument is a property path over the `Where` parameter, SHALL be a strong bound. A key-equality predicate of the form `where entity => entity.Key == value`, where the property resolves through EF model metadata to a primary key, foreign key, or alternate key, SHALL be a strong bound. The same equality shape whose key status is established only by an `Id`, `<Entity>Id`, or `*Id` name heuristic SHALL be a medium bound. Bound recognition MUST use the shared predicate model and MUST remain linear in the inspected chain without general data-flow or interprocedural analysis.

#### Scenario: Local collection membership
- **WHEN** an inline EF query filters with `Where(x => ids.Contains(x.ProductId))` over a local collection
- **THEN** EFD005 recognizes a strong bound and does not report

#### Scenario: Membership via Any
- **WHEN** an inline EF query filters with `Where(x => ids.Any(id => id == x.Fk))` over a local collection
- **THEN** EFD005 recognizes a strong bound and does not report

#### Scenario: Key-equality bound from metadata
- **WHEN** an inline EF query filters with `Where(x => x.Id == value)` where the property is a key resolved from EF metadata
- **THEN** EFD005 recognizes a strong bound and does not report

#### Scenario: Key-equality bound by name heuristic
- **WHEN** an inline EF query filters with `Where(x => x.CustomerId == value)` where key status is established only by the name heuristic
- **THEN** EFD005 recognizes a medium bound and reports at reduced confidence rather than suppressing

#### Scenario: Time-window intent signal
- **WHEN** an inline EF query filters with a temporal comparison such as `Where(x => x.CreatedUtc >= since)`
- **THEN** EFD005 recognizes a weak bound and reports at advisory confidence

### Requirement: Bound-neutral composition does not change the outcome
EFD005 SHALL produce an identical outcome for two otherwise identical queries that differ only by a bound-neutral composition method. `AsSplitQuery`, `AsSingleQuery`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `Include`, `ThenInclude`, and `TagWith` are bound-neutral and MUST NOT establish, remove, or alter a recognized row bound.

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

### Requirement: Assign EFD005 confidence by blast radius
EFD005 SHALL assign confidence from the recognized bound strength and best-effort context signals rather than a fixed level. A finding with no recognized bound, an entity that is not a known small lookup, and a hot-path call site SHALL be high confidence. A finding with a medium-strength bound, or with no bound but without the hot-path or entity signals, SHALL be medium confidence. A finding with a weak-strength bound SHALL be advisory confidence. When a context signal cannot be determined statically, EFD005 SHALL default to medium confidence and MUST NOT over-claim. The universal test-project downgrade defined in the CLI analysis capability applies after this rule-assigned confidence and may further reduce it.

#### Scenario: Hot-path unbounded query
- **WHEN** an unbounded EF query is materialized in a handler, controller, or service method over an entity that is not a known small lookup
- **THEN** EFD005 reports at high confidence

#### Scenario: Weak bound downgrade
- **WHEN** the only recognized bound is a weak-strength time-window predicate
- **THEN** EFD005 reports at advisory confidence

#### Scenario: Unknown context defaults to medium
- **WHEN** the call-site and entity signals cannot be determined statically and no strong or weak bound is recognized
- **THEN** EFD005 reports at medium confidence
