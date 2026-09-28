# EFD005 Unbounded Query Materialization Specification

## Purpose

Define a qualified, semantic warning for EF Core list materialization whose inline database query has no recognized query-side row bound.

## Requirements

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

### Requirement: Recognize predicate-derived row bounds
EFD005 SHALL recognize row bounds established by the predicate of a resolved `Queryable.Where` in the proven inline chain, in addition to `Queryable.Take`. A local-collection membership predicate of the form `where entity => collection.Contains(entity.Property)` or `where entity => collection.Any(...)`, whose receiver is a local, parameter, or field collection and whose argument is a property path over the `Where` parameter, SHALL be a strong bound. A key-equality predicate of the form `where entity => entity.Key == value`, where the property resolves through EF model metadata to a primary key, foreign key, or a unique single-column alternate key, SHALL be a strong bound. Membership of a non-unique index, or of one column of a composite key or index, SHALL NOT by itself be a strong bound.

EF model metadata SHALL be any of the following sources:
- **Data annotations** on the entity: `[Key]`, `[ForeignKey]`, and a unique single-column `[Index]`.
- **Fluent configuration in the analyzed project**, whether in `OnModelCreating` or in an `IEntityTypeConfiguration<T>` implementation, in lambda or string form. The recognized calls are a single-column `HasKey`, a single-column `HasAlternateKey`, a single-column `HasForeignKey`, and a single-column `HasIndex` followed by `IsUnique()`. Fluent configuration applies to the entity type it names and to types derived from it.
- **EF Core's navigation foreign-key convention.** A property named `<Navigation>Id` is a foreign key when the entity type, or one of its base types, declares a reference navigation named `<Navigation>`. A reference navigation is a property whose type is a class other than `string` and is not a collection.

Configuration that is not visible in the analyzed project SHALL NOT be assumed. That includes configuration compiled into a referenced assembly, and a migration snapshot that names entity types by string. The `Id` and `<Entity>Id` primary-key convention SHALL NOT by itself prove a key, because a composite key configured elsewhere cannot be ruled out. The same equality shape whose key status is established only by an `Id`, `<Entity>Id`, or `*Id` name heuristic SHALL be a medium bound.

Equality SHALL be recognized in each of these forms, with the same bound strength:
- the built-in `==` operator;
- a user-defined `==` operator, including its lifted nullable form, whose two parameters have the entity key property's type, ignoring nullability. This covers `Guid`, `DateTimeOffset`, and other value types that define their own equality operator;
- a single-argument instance `Equals` call whose receiver is one side of the comparison and whose argument is the other.

The same operator forms SHALL apply to the equality inside a membership predicate written with `Any`.

In a key-equality predicate, the compared `value` SHALL be any single scalar value that is not a property path over the `Where` entity parameter. That includes:
- a constant, a local, a non-entity parameter, a field, or a default-value expression;
- a property or member access whose root is a different object, for example `parent.Id`, `product.ProductId`, or `request.Filter.OwnerId`.

A property or member access on the compared side MUST NOT be required to be a constant, local, parameter, or field to establish the bound. The bound strength SHALL be determined solely by the entity-side key (metadata versus name heuristic), never by the kind of the comparand or the equality form. Exactly one side of the equality SHALL be an entity key path over the `Where` parameter; when both sides are property paths over the `Where` parameter, or neither side is, no key-equality bound is established.

When a predicate is a conjunction, EFD005 SHALL take the strongest bound among its conjuncts; a null-guarded nullable value access such as `entity.Property.Value` SHALL resolve to the underlying entity property. Bound recognition MUST use the shared predicate model, MUST NOT alter the shared EFD004 scalar predicate model, and MUST remain linear in the inspected chain without general data-flow or interprocedural analysis.

#### Scenario: Local collection membership
- **WHEN** an inline EF query filters with `Where(x => ids.Contains(x.ProductId))` over a local collection
- **THEN** EFD005 recognizes a strong bound and does not report

#### Scenario: Membership via Any
- **WHEN** an inline EF query filters with `Where(x => ids.Any(id => id == x.Fk))` over a local collection
- **THEN** EFD005 recognizes a strong bound and does not report

#### Scenario: Membership within a conjunction
- **WHEN** an inline EF query filters with `Where(x => ids.Contains(x.Fk) && x.OtherFilter)` combining membership with additional conditions
- **THEN** EFD005 recognizes the membership conjunct as a strong bound and does not report

#### Scenario: Null-guarded membership over a nullable key
- **WHEN** an inline EF query filters with `Where(x => x.OptionalFk.HasValue && ids.Contains(x.OptionalFk.Value))`
- **THEN** EFD005 resolves the nullable value access to the entity property, recognizes a strong bound, and does not report

#### Scenario: Key-equality bound from metadata
- **WHEN** an inline EF query filters with `Where(x => x.Id == value)` where the property is a key resolved from EF metadata
- **THEN** EFD005 recognizes a strong bound and does not report

#### Scenario: Non-unique or composite index is not a strong key bound
- **WHEN** an inline EF query filters with `Where(x => x.Column == value)` where `Column` participates only in a non-unique or composite index
- **THEN** EFD005 does not treat it as a strong key-equality bound

#### Scenario: Key from fluent configuration
- **WHEN** an entity's key is declared with `HasKey(e => e.Code)` or `HasKey("Code")` in `OnModelCreating` or an `IEntityTypeConfiguration<T>` in the analyzed project, and an inline EF query filters with `Where(x => x.Code == value)`
- **THEN** EFD005 recognizes a strong key-equality bound and does not report

#### Scenario: Foreign key from fluent configuration
- **WHEN** a relationship declares `HasForeignKey(e => e.OwnerRef)` in the analyzed project, and an inline EF query filters with `Where(x => x.OwnerRef == ownerId)`
- **THEN** EFD005 recognizes a strong key-equality bound and does not report

#### Scenario: Unique index from fluent configuration
- **WHEN** the analyzed project declares `HasIndex(e => e.Email).IsUnique()`, and an inline EF query filters with `Where(x => x.Email == email)`
- **THEN** EFD005 recognizes a strong key-equality bound and does not report

#### Scenario: Composite or non-unique fluent configuration is not a strong key
- **WHEN** the only configuration for a property is a composite `HasKey`, a composite `HasForeignKey`, or a `HasIndex` without `IsUnique()`
- **THEN** EFD005 does not treat the property as a proven key, and a name-matched property stays a medium bound reported at advisory confidence

#### Scenario: Foreign key by navigation convention
- **WHEN** an entity declares a reference navigation `Organization` and a property `OrganizationId`, and an inline EF query filters with `Where(x => x.OrganizationId == organizationId)`
- **THEN** EFD005 recognizes a strong key-equality bound and does not report, even when no fluent configuration is visible

#### Scenario: Primary-key naming convention alone is not a proven key
- **WHEN** an inline EF query filters with `Where(x => x.Id == id)` and no data annotation, visible fluent configuration, or navigation convention proves `Id` is a key
- **THEN** EFD005 treats it as a medium bound and reports at advisory confidence

#### Scenario: Key-equality bound by name heuristic
- **WHEN** an inline EF query filters with `Where(x => x.CustomerId == value)` where key status is established only by the name heuristic
- **THEN** EFD005 recognizes a medium bound and reports at advisory confidence rather than suppressing

#### Scenario: Key-equality through a user-defined equality operator
- **WHEN** an inline EF query filters with `Where(x => x.Key == id)` where the key and `id` are a `Guid` or another value type that defines its own `==` operator
- **THEN** EFD005 recognizes the key-equality bound at the same strength as for a built-in `==`: strong for a metadata key (not reported) and medium for a name-heuristic key (reported at advisory confidence)

#### Scenario: Key-equality through a lifted nullable operator
- **WHEN** an inline EF query filters with `Where(x => x.OptionalKey == id)` where `OptionalKey` is a nullable `Guid` key
- **THEN** EFD005 recognizes the key-equality bound as it would for the non-nullable key

#### Scenario: Key-equality through Equals
- **WHEN** an inline EF query filters with `Where(x => x.Key.Equals(id))` or `Where(x => id.Equals(x.Key))`
- **THEN** EFD005 recognizes the key-equality bound at the same strength as for `x.Key == id`

#### Scenario: Membership via Any with a user-defined equality operator
- **WHEN** an inline EF query filters with `Where(x => ids.Any(id => id == x.Fk))` over a local collection of `Guid` values
- **THEN** EFD005 recognizes a strong bound and does not report

#### Scenario: Equals between two entity properties
- **WHEN** an inline EF query filters with `Where(x => x.Key.Equals(x.OtherProperty))`
- **THEN** EFD005 does not establish a key-equality bound and reports if no other bound applies

#### Scenario: Key-equality against a property of another entity
- **WHEN** an inline EF query filters with `Where(x => x.ProductId == product.ProductId)` where `x.ProductId` is an entity key path over the `Where` parameter and `product.ProductId` is a property access on a different object
- **THEN** EFD005 recognizes the key-equality bound at the same strength it would for a constant or local on the compared side and does not report differently because the comparand is a property access

#### Scenario: Key-equality against a nested member of a parameter
- **WHEN** an inline EF query filters with `Where(x => x.OwnerId == request.Filter.OwnerId)` where `request` is a method parameter and `request.Filter.OwnerId` is a nested member access resolving to a metadata key on the entity side
- **THEN** EFD005 recognizes the strong key-equality bound and does not report

#### Scenario: Key-equality with a compound predicate and a property comparand
- **WHEN** an inline EF query filters with `Where(x => x.ProductId == product.ProductId && x.ActionName == SomeEnum.Submit)` where `x.ProductId` resolves to a strong key
- **THEN** EFD005 recognizes the key-equality bound from the key-equality conjunct and does not report

#### Scenario: Both sides are entity property paths
- **WHEN** an inline EF query filters with `Where(x => x.Key == x.OtherProperty)` where both comparands are property paths over the `Where` parameter
- **THEN** EFD005 does not establish a key-equality bound because neither side is a scalar value external to the entity, and reports if no other bound applies

#### Scenario: Time-window intent signal
- **WHEN** an inline EF query filters with a temporal comparison such as `Where(x => x.CreatedUtc >= since)`
- **THEN** EFD005 recognizes a weak bound and reports at advisory confidence

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

### Requirement: Assign EFD005 confidence by blast radius
EFD005 SHALL assign confidence from the recognized bound strength and best-effort context signals rather than a fixed level. Confidence SHALL be assigned as follows:
- **High:** no recognized bound, an entity that is not a known small lookup, and a hot-path call site.
- **Medium:** no recognized bound, without the hot-path or entity signals.
- **Advisory:** a medium-strength bound (a key-by-name equality) or a weak-strength bound (a time window). The finding is still reported so the choice stays visible. The query most likely loads one row, or the children of one parent, which the remediation already names as legitimate.

When a context signal cannot be determined statically, EFD005 SHALL default to medium confidence and MUST NOT over-claim. The universal test-project downgrade defined in the CLI analysis capability applies after this rule-assigned confidence and may further reduce it.

#### Scenario: Hot-path unbounded query
- **WHEN** an unbounded EF query is materialized in a handler, controller, or service method over an entity that is not a known small lookup
- **THEN** EFD005 reports at high confidence

#### Scenario: Medium bound downgrade
- **WHEN** the only recognized bound is a medium-strength key-by-name equality
- **THEN** EFD005 reports at advisory confidence, even in a hot-path call site

#### Scenario: Weak bound downgrade
- **WHEN** the only recognized bound is a weak-strength time-window predicate
- **THEN** EFD005 reports at advisory confidence

#### Scenario: Unknown context defaults to medium
- **WHEN** the call-site and entity signals cannot be determined statically and no strong, medium, or weak bound is recognized
- **THEN** EFD005 reports at medium confidence

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

### Requirement: Avoid duplicate EFD019 findings at one materializer
EFD005 SHALL NOT report a materializer when the same invocation is eligible for EFD019 materialize-then-reduce analysis. EFD019 SHALL remain the more specific explanation when the buffered result is immediately reduced to an element, count, existence check, or aggregate.

#### Scenario: Immediate reduction after ToList
- **WHEN** an unbounded materializer is immediately consumed by a supported EFD019 reducer such as `First()` or `Count()`
- **THEN** EFD019 may report and EFD005 does not report at that materializer

#### Scenario: Unsupported reduction
- **WHEN** an eligible unbounded materializer is immediately consumed by a reducer overload or lambda that EFD019 cannot classify as SQL-capable
- **THEN** EFD005 may report because no duplicate EFD019 finding exists

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
