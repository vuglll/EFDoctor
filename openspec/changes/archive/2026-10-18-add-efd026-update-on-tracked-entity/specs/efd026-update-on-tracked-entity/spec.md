## Purpose

Define advisory detection of `Update` called on an entity that the same `DbContext` already tracks because the method loaded it with tracking, which marks every column modified for no benefit.

## ADDED Requirements

### Requirement: Detect Update on an entity the context already tracks
EFD026 SHALL report a resolved `DbContext.Update`, `DbContext.Update<TEntity>`, or `DbSet<TEntity>.Update` invocation when all of these hold:
- **The argument:** a read of a local that is declared, with an initializer, by a statement directly in a block that encloses the invocation, in the same method, lambda, or local function, before the statement that contains the invocation.
- **The tracked load:** the initializer is one of these:
  - a resolved `DbSet<TEntity>.Find` or `FindAsync`, awaited when asynchronous;
  - a resolved `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, or `LastOrDefault`, or its awaited EF Core async form, over a proven EF Core query whose chain has no projection (`Select`, `SelectMany`, `GroupBy`, `Join`, or `GroupJoin`), and no `AsNoTracking` or `AsNoTrackingWithIdentityResolution` after the last `AsTracking`.
- **The context:** the load's context and the `Update` receiver's context have the same symbol key.
- **No tracker changes:** no statement between the load and the `Update` calls `ChangeTracker.Clear()` on that context, assigns `Entry(…).State`, or writes the local.

EFD026 SHALL anchor the finding on the `Update` invocation.

#### Scenario: Update after a tracked query
- **WHEN** `var order = db.Orders.First(o => o.Id == id); order.Status = 2; db.Update(order);`
- **THEN** EFD026 reports `db.Update(order)`

#### Scenario: Find and DbSet Update
- **WHEN** `var order = await db.Orders.FindAsync(id); db.Orders.Update(order!);`
- **THEN** EFD026 reports the `Update` invocation

#### Scenario: Untracked or external entity
- **WHEN** the entity is a parameter, built from a DTO, projected with `Select(o => new Order { … })`, or loaded with `AsNoTracking()`
- **THEN** EFD026 does not report

#### Scenario: Tracker cleared or entity detached
- **WHEN** `db.ChangeTracker.Clear()` runs, or `db.Entry(order).State` is assigned, between the load and the `Update`
- **THEN** EFD026 does not report

#### Scenario: Different context
- **WHEN** the entity is loaded from one context instance and updated on another
- **THEN** EFD026 does not report

### Requirement: Produce advisory EFD026 findings
Each EFD026 finding SHALL have:
- rule title `Update on an entity the context already tracks`;
- `Performance` category, `Info` severity, and advisory confidence;
- documentation key `EFD026`;
- the message `'{entity}' is already tracked, so Update is unnecessary and marks every column modified`.

Evidence SHALL name the load and the context. Impact SHALL explain the full-row `UPDATE`. Remediation SHALL recommend removing the call.

#### Scenario: Complete structured finding
- **WHEN** EFD026 reports an invocation
- **THEN** the finding has the title, category, severity, confidence, documentation key, message, evidence, impact, and remediation described above

### Requirement: Support standard suppression and existing reporting behavior
EFD026 SHALL use standard Roslyn suppression, and flow through the existing CLI, console and JSON output, and exit codes.

#### Scenario: Suppression
- **WHEN** a matching invocation is covered by `#pragma warning disable EFD026`, `[SuppressMessage("Performance", "EFD026")]`, or `dotnet_diagnostic.EFD026.severity = none`
- **THEN** the finding is excluded

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan of the EFD026 fixture project runs in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD026 findings, with exact locations, JSON schema version `1`, and exit code `1`
