# Spec Delta

## Purpose

Defines conservative detection and reporting for EF Core load/loop/save patterns that can be reviewed for replacement by set-based `ExecuteUpdate` or `ExecuteDelete` operations.

## ADDED Requirements

### Requirement: Detect uniform load-modify-save update patterns
EFD013 SHALL report a medium-confidence finding when a semantically proven EF Core entity query is materialized, the resulting sequence is consumed by a same-block `foreach` whose body consists only of one or more direct assignments to writable scalar properties of the iteration entity, and the loop is immediately followed by `SaveChanges` or an awaited `SaveChangesAsync` on the query's owning context. Every assigned value MUST be independent of the current iteration entity and represent one stable value for all matched rows.

#### Scenario: Synchronous uniform update loop
- **WHEN** a local sequence is initialized by `ToList` over a proven EF Core query, a following `foreach` assigns a constant, parameter, or stable local value to each entity's direct scalar property, and the owning context immediately calls `SaveChanges`
- **THEN** EFD013 reports the materialize/loop/save pattern as a candidate for `ExecuteUpdate`

#### Scenario: Asynchronous uniform update loop
- **WHEN** a local sequence is initialized by an awaited `ToListAsync` over a proven EF Core query, a following `foreach` performs only eligible uniform assignments, and the owning context immediately awaits `SaveChangesAsync`
- **THEN** EFD013 reports the pattern as a candidate for `ExecuteUpdateAsync`

#### Scenario: Multiple uniform property assignments
- **WHEN** the loop body assigns eligible stable values to multiple direct scalar properties and performs no other work
- **THEN** EFD013 produces one finding for the complete update pattern rather than one finding per assignment

### Requirement: Detect load-delete-save patterns
EFD013 SHALL report a medium-confidence finding when a semantically proven EF Core entity query is materialized, the resulting sequence is consumed by a same-block `foreach` whose body consists only of a semantically resolved `DbContext.Remove` or originating `DbSet.Remove` call for the iteration entity, and the loop is immediately followed by `SaveChanges` or an awaited `SaveChangesAsync` on the same owning context.

#### Scenario: Delete each loaded entity through the context
- **WHEN** a proven EF Core query is materialized, each resulting entity is passed directly to the owning context's `Remove` method, and that context immediately saves after the loop
- **THEN** EFD013 reports the pattern as a candidate for `ExecuteDelete` or `ExecuteDeleteAsync`

#### Scenario: Delete each loaded entity through its set
- **WHEN** a proven EF Core query is materialized, each resulting entity is passed directly to `Remove` on the originating `DbSet`, and the owning context immediately saves after the loop
- **THEN** EFD013 reports one delete-pattern finding

### Requirement: Prove a single same-block operation chain
EFD013 SHALL correlate the query origin, materialization, loop source, loop action, and save operation by resolved symbols and a single unambiguous same-block statement sequence. It SHALL support a directly materialized loop source or a sequence local with exactly one eligible initializer and no reassignment or escape before the loop, and SHALL require the save receiver to be the same context instance that owns the query.

#### Scenario: Unrelated same-named operations
- **WHEN** application methods named `ToListAsync`, `Remove`, or `SaveChangesAsync` form a superficially similar statement sequence but do not resolve to supported EF Core APIs
- **THEN** EFD013 does not report

#### Scenario: Different context saves the changes
- **WHEN** the query originates from one context instance but the post-loop save call targets another context instance
- **THEN** EFD013 does not report

#### Scenario: Materialized sequence has ambiguous provenance
- **WHEN** the loop sequence is reassigned, passed by reference, returned by a helper, stored in a field, or selected through conditional control flow
- **THEN** EFD013 does not report because one eligible source cannot be proven

### Requirement: Require bulk-operation support
EFD013 SHALL report an update or delete candidate only when the analyzed compilation exposes the corresponding EF Core `ExecuteUpdate`/`ExecuteUpdateAsync` or `ExecuteDelete`/`ExecuteDeleteAsync` API needed by the recommended replacement.

#### Scenario: Project references pre-bulk EF Core APIs
- **WHEN** a project contains an otherwise matching pattern but its resolved EF Core references do not expose the corresponding bulk operation
- **THEN** EFD013 does not report that candidate

### Requirement: Exclude patterns with uncertain behavioral equivalence
EFD013 SHALL NOT report when the loop contains conditionals, control-flow transfers, method calls, reads or writes outside the direct eligible action, nested loops, exception handling, or a save operation inside the loop. It SHALL also exclude update assignments that read the iteration entity, target nested or navigation members, use compound or increment/decrement assignment, or cannot be represented as a stable set-property value.

#### Scenario: Update depends on the current entity
- **WHEN** a loop assigns a value calculated from a property of the current iteration entity
- **THEN** EFD013 does not report in the initial rule scope

#### Scenario: Loop contains business behavior
- **WHEN** an otherwise eligible update or delete loop also invokes a service, emits an event, logs per entity, branches, or mutates another value
- **THEN** EFD013 does not report

#### Scenario: Save occurs inside the loop
- **WHEN** `SaveChanges` or `SaveChangesAsync` executes within the entity loop
- **THEN** EFD013 does not report that pattern, leaving the distinct per-iteration-save concern to EFD001

#### Scenario: Work occurs between the loop and save
- **WHEN** another executable statement occurs between the loop and the owning context's save operation
- **THEN** EFD013 does not report because the operation chain is no longer the required immediate sequence

### Requirement: Produce actionable and qualified findings
Each EFD013 finding SHALL locate the materialization expression, identify whether the candidate is an update or delete, name the resolved query, loop action, and save evidence, use medium confidence and warning severity, and describe row transfer, change-tracking work, and per-row command generation as likely rather than measured costs. Remediation SHALL advise reviewing `ExecuteUpdate`/`ExecuteDelete` while explicitly warning that bulk operations bypass tracked-entity state and `SaveChanges` behavior and can differ in concurrency-token handling, interceptors, domain callbacks, cascade behavior, and transaction boundaries.

#### Scenario: Update finding contract
- **WHEN** EFD013 reports an eligible update pattern
- **THEN** the finding contains precise source coordinates, non-empty evidence for the materialization/assignments/save chain, qualified impact, guarded `ExecuteUpdate` remediation, and documentation key `EFD013`

#### Scenario: Delete finding contract
- **WHEN** EFD013 reports an eligible delete pattern
- **THEN** the finding distinguishes deletion from update and includes guarded `ExecuteDelete` remediation without claiming exact runtime savings or automatic semantic equivalence

### Requirement: Support standard suppression and generated-code exclusion
EFD013 SHALL honor standard Roslyn suppression and SHALL exclude generated code consistently with the other source analyzers.

#### Scenario: Suppressed finding
- **WHEN** an otherwise matching EFD013 pattern is suppressed by pragma, analyzer configuration, or `SuppressMessage`
- **THEN** EFD013 emits no finding for that pattern

#### Scenario: Generated source
- **WHEN** an otherwise matching pattern appears in generated source
- **THEN** EFD013 emits no finding

### Requirement: Validate precision with representative fixtures
EFD013 SHALL have at least ten positive and ten negative analyzer fixtures spanning synchronous and asynchronous update/delete forms, multiple assignments, direct and local materialization, unrelated APIs, mismatched contexts, ambiguous sequence provenance, entity-dependent values, extra loop behavior, bulk-API availability, exact locations, diagnostic properties, generated code, and suppression.

#### Scenario: Curated validation suite
- **WHEN** the EFD013 analyzer fixture suite runs
- **THEN** every curated eligible case reports once and every curated excluded, ambiguous, unsupported, or unrelated case remains clean
