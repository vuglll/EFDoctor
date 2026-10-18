# efd032-untracked-change-lost Specification

## Purpose
Define high-confidence detection of a property change to an entity loaded with `AsNoTracking`, followed by `SaveChanges` on the same context, which saves nothing because the entity is not tracked.

## Requirements

### Requirement: Detect a change to an untracked entity before SaveChanges
EFD032 SHALL report the first property assignment to an untracked entity when all of these hold:
- **The load:** a local is declared, with an initializer, by a statement directly in a block. The initializer is a resolved single-entity terminal (`First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, or `LastOrDefault`, or the awaited EF Core async form of one) over a proven EF Core query. Alternatively, the initializer is `ToList`, `ToArray`, `ToListAsync`, or `ToArrayAsync` over such a query, and a later statement is a `foreach` whose collection is that local. In that case, the entity is the `foreach` iteration variable.
- **The query:** its proven chain contains `AsNoTracking` or `AsNoTrackingWithIdentityResolution`, and its `DbContext` instance has a symbol key, as for EFD038.
- **The change:** a statement after the load assigns a property of the entity through a simple, compound, coalescing, increment, or decrement assignment. For a list, the assignment is inside that `foreach` body.
- **The save:** a later statement of the same block calls `SaveChanges` or `SaveChangesAsync` on the same `DbContext` instance.
- **Nothing attaches the entity:** between the load and the save, every reference to the entity local is either the instance of a property read or write, or a comparison with `null` (`==`, `!=`, `is null`, or `is not null`). Every reference to the list local is the `foreach` collection, or the instance of a property read such as `Count`.

EFD032 SHALL report once per load, at the first assignment.

#### Scenario: Single entity changed and saved
- **WHEN** `var order = db.Orders.AsNoTracking().First(o => o.Id == id); order.Status = 2; db.SaveChanges();`
- **THEN** EFD032 reports `order.Status = 2`

#### Scenario: Awaited async load and save
- **WHEN** the load is `await db.Orders.AsNoTracking().FirstOrDefaultAsync(...)` and the save is `await db.SaveChangesAsync()`
- **THEN** EFD032 reports the first assignment

#### Scenario: Entities of a list changed in a foreach
- **WHEN** `var orders = db.Orders.AsNoTracking().Where(...).ToList(); foreach (var order in orders) { order.Open = false; } db.SaveChanges();`
- **THEN** EFD032 reports `order.Open = false`

#### Scenario: Compound and increment changes
- **WHEN** the change is `order.Total += 5` or `order.Version++`
- **THEN** EFD032 reports it

#### Scenario: Entity attached or passed on
- **WHEN** between the load and the save the method calls `db.Update(order)`, `db.Attach(order)`, `db.Entry(order)`, `db.Add(order)`, or passes `order` to any other method, returns or stores it, captures it in a lambda, or reassigns the local
- **THEN** EFD032 does not report

#### Scenario: Tracked load
- **WHEN** the query has no `AsNoTracking`, or uses `AsTracking`
- **THEN** EFD032 does not report

#### Scenario: Save on another context or no save
- **WHEN** the save is on a different context instance, or the method doesn't save after the change
- **THEN** EFD032 does not report

### Requirement: Produce actionable EFD032 findings
Each EFD032 finding SHALL have:
- rule title `Change to an untracked entity is never saved`;
- `Correctness` category, `Warning` severity, and high confidence;
- documentation key `EFD032`;
- the message `'{entity}' was loaded with {method}, so SaveChanges does not save this change`.

Evidence SHALL name the load, the context, and the line of the save. Impact SHALL explain that `SaveChanges` writes nothing for an untracked entity, and raises no error. Remediation SHALL recommend three fixes:
- remove `AsNoTracking` from a load that is meant for an update;
- call `Update` or `Attach` before changing the entity;
- use `ExecuteUpdate`.

#### Scenario: Complete structured finding
- **WHEN** EFD032 reports an assignment
- **THEN** the finding has the title, category, severity, confidence, documentation key, message, evidence, impact, and remediation described above

### Requirement: Support standard suppression and existing reporting behavior
EFD032 SHALL use standard Roslyn suppression, and flow through the existing CLI, console and JSON output, and exit codes.

#### Scenario: Suppression
- **WHEN** a matching assignment is covered by `#pragma warning disable EFD032`, `[SuppressMessage("Correctness", "EFD032")]`, or `dotnet_diagnostic.EFD032.severity = none`
- **THEN** the finding is excluded

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan of the EFD032 fixture project runs in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD032 findings, with exact locations, JSON schema version `1`, and exit code `1`
