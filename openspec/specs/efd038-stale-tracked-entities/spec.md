# efd038-stale-tracked-entities Specification

## Purpose
Defines detection and reporting of EF Core bulk operations that run after entities of the same type were loaded with tracking on the same `DbContext` instance, leaving those tracked entities stale.

## Requirements

### Requirement: Detect a bulk operation after a tracked load of the same entity type
EFD038 SHALL report a semantically resolved EF Core `ExecuteUpdate`, `ExecuteUpdateAsync`, `ExecuteDelete`, or `ExecuteDeleteAsync` whose query is proven to originate from a `DbSet<TEntity>` or `DbContext.Set<TEntity>()`, when an earlier statement of an enclosing block is a tracked load of `TEntity` from the same `DbContext` instance.

A tracked load is a local declaration whose initializer, after any `await`, is `DbSet<TEntity>.Find` or `FindAsync`, or is `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `ToList`, `ToArray`, or an EF Core async form of one, on a proven query whose element type is `TEntity` and whose chain has no `AsNoTracking` or `AsNoTrackingWithIdentityResolution`.

The load statement SHALL be directly inside a block, and the bulk operation SHALL be in a later statement of that block, at any depth, outside any lambda or local function. The same instance SHALL be proven by symbol: a never-written local or parameter, a `this` or static field or auto-property, or `this`. Methods SHALL be identified by resolved symbols.

#### Scenario: Find then ExecuteUpdate
- **WHEN** `var product = await db.Products.FindAsync(id);` is followed by `await db.Products.Where(p => p.Id == id).ExecuteUpdateAsync(…)`
- **THEN** EFD038 reports the `ExecuteUpdateAsync` invocation

#### Scenario: Query terminals as loads
- **WHEN** the local is initialized by `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `ToList`, `ToArray`, or an async form, on a tracked query of the entity type
- **THEN** EFD038 reports a later bulk operation on that entity type and context

#### Scenario: ExecuteDelete
- **WHEN** a tracked load is followed by `ExecuteDelete` or `ExecuteDeleteAsync` on the same entity type and context
- **THEN** EFD038 reports the bulk operation

#### Scenario: Bulk operation nested in a later statement
- **WHEN** the bulk operation is inside an `if`, a loop, or a `try` that follows the load in the same block
- **THEN** EFD038 reports it

#### Scenario: Context held in a field or property
- **WHEN** the load and the bulk operation both use the same `this` field, auto-property, or `this` context
- **THEN** EFD038 reports the bulk operation

### Requirement: Stay silent when staleness is not proven
EFD038 MUST NOT report when the load is not tracked, when it loads a different entity type or a projection, when the context instances are different or cannot be compared by symbol, when the load does not precede the bulk operation in an enclosing block, when either is inside a lambda or local function, when the load's filter and the bulk operation's filter each require the same property to equal a constant and the constants differ, or when a statement after the load in the load's block calls `ChangeTracker.Clear()`, `Entry(…).Reload()` or `ReloadAsync()`, or sets `Entry(…).State` to `EntityState.Detached` on the same context.

#### Scenario: No-tracking load
- **WHEN** the load uses `AsNoTracking` or `AsNoTrackingWithIdentityResolution`
- **THEN** EFD038 does not report

#### Scenario: Different entity type or projection
- **WHEN** the load returns another entity type, or a projection of the entity
- **THEN** EFD038 does not report

#### Scenario: Different or unprovable context
- **WHEN** the load and the bulk operation use different context instances, a reassigned context local, or a context reached through a method call or another object's member
- **THEN** EFD038 does not report

#### Scenario: Order not proven
- **WHEN** the load comes after the bulk operation, or the two are in sibling branches, or one is inside a lambda or local function
- **THEN** EFD038 does not report

#### Scenario: Tracker cleared, or entity reloaded or detached
- **WHEN** a statement after the load calls `ChangeTracker.Clear()`, `Entry(entity).Reload()`, `Entry(entity).ReloadAsync()`, or sets `Entry(entity).State = EntityState.Detached` on the same context
- **THEN** EFD038 does not report

#### Scenario: Filters that cannot overlap
- **WHEN** the load filters with `x.Name == "a"` and the bulk operation filters with `x.Name == "b"`, as top-level conjuncts of predicates in their inline query chains
- **THEN** EFD038 does not report, and it still reports when a constant is replaced by a variable, the constants are equal, or the comparison is not a top-level equality

#### Scenario: Bulk operation alone
- **WHEN** a method runs a bulk operation and loads nothing of that type before it
- **THEN** EFD038 does not report

### Requirement: Raise confidence when the stale state is used
EFD038 SHALL report with high confidence when, after the bulk operation and within the remaining statements of the load's block, the method references the loaded local, performs another tracked load of the same entity type from the same context, or calls `SaveChanges` or `SaveChangesAsync` on the same context when a member of a loaded entity is written after the load. A loaded entity is the local itself, a `foreach` variable over it, or an indexed element of it. Otherwise it SHALL report with medium confidence.

#### Scenario: Loaded entity read afterwards
- **WHEN** the loaded local is referenced after the bulk operation
- **THEN** the finding has high confidence, and the evidence names the reference

#### Scenario: Same type loaded again
- **WHEN** `Find`, `FindAsync`, or a tracked query terminal loads the same entity type from the same context after the bulk operation
- **THEN** the finding has high confidence, and the evidence says the load returns the tracked instance

#### Scenario: Modified entity saved afterwards
- **WHEN** a member of a loaded entity is written, and `SaveChanges` or `SaveChangesAsync` is called on the same context after the bulk operation
- **THEN** the finding has high confidence, and the evidence names the save

#### Scenario: Left stale
- **WHEN** nothing after the bulk operation uses the loaded local or loads the type again, and no modified loaded entity is saved, including when `SaveChanges` runs but no loaded entity was written
- **THEN** the finding has medium confidence

### Requirement: Report evidence-based findings
Each EFD038 finding SHALL use the `Correctness` category, be located on the complete bulk-operation invocation, and be reported once per bulk operation. The evidence SHALL name the loaded local, the load expression, the entity type, the context, and what follows the bulk operation. The finding SHALL explain that bulk operations bypass the change tracker, and SHALL recommend running the bulk operation before loading, loading without tracking, clearing the tracker, reloading or detaching the entities, or using a separate context, and SHALL name the case where the bulk filter cannot match the loaded rows. A high-confidence finding SHALL have `Warning` severity, and a medium-confidence one `Info` severity in a build. Standard Roslyn suppression SHALL apply, and generated code SHALL NOT be analyzed.

#### Scenario: Complete structured finding
- **WHEN** EFD038 reports
- **THEN** the diagnostic has the documented severity for its confidence, evidence naming the local, the load, the entity type, and the context, the impact, the remediation, and the documentation key `EFD038`

#### Scenario: Diagnostic location
- **WHEN** EFD038 reports an awaited `ExecuteUpdateAsync`
- **THEN** the location spans the invocation from the start of the query to the closing parenthesis, without the `await`

#### Scenario: One finding per bulk operation
- **WHEN** two tracked loads precede one bulk operation
- **THEN** EFD038 reports once, naming the closest load

#### Scenario: Standard suppression
- **WHEN** the finding is suppressed with a pragma, `SuppressMessage`, or a configured `none` severity
- **THEN** it is not reported

#### Scenario: Generated code
- **WHEN** the pattern occurs in generated code
- **THEN** EFD038 does not report
