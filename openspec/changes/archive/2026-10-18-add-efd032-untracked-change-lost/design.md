# Design

## Context

EFD038 already finds a tracked load into a local by walking the statements of an enclosing block, and resolves the context by symbol through `EfContextIdentity`. EFD032 is its mirror: it looks for untracked loads, then a write, then a save.

## Goals / Non-Goals

**Goals:** a high-confidence finding for the straight-line load–change–save shape, for one entity or a list iterated by `foreach`.

**Non-Goals:**
- entities that leave the method;
- changes made in helpers;
- context-wide no-tracking defaults (`UseQueryTrackingBehavior`), which the analyzer can't see from the query;
- conditional tracking.

## Decisions

### Entry point: the save

**Decision:** Register on invocations of `SaveChanges`/`SaveChangesAsync` on a `DbContext`. Walk the save's enclosing blocks, up to the nearest function, for an earlier declaration statement that is an untracked load on the same context key.

**Why:** It mirrors EFD038, and ties every finding to a save that would have written the change.

### "Nothing attaches the entity" is a reference whitelist

**Decision:** Collect every reference to the entity local, or the list local, in the statements from the load to the save. Accept only these references:
- the entity, as the `Instance` of a property reference;
- the list, as a `foreach` collection.

Anything else is a bail-out: an argument, a return, an assignment value, a lambda capture, or a write to the local.

**Why:** Any other use might attach the entity, for example a helper that calls `Update`. A whitelist keeps the confidence high without a list of attaching methods.

### Tracking is read from the full chain proof

**Decision:** Prove the query with the full chain proof, not the origin proof, and require `EF.AsNoTracking*` in the proven operations.

**Why:** A conditional branch could add `AsTracking()`. The full proof follows only statically determined locals, so the no-tracking state holds on every path.

## Risks / Trade-offs

- **[Risk] A `SaveChanges` override or an interceptor attaches untracked entities.** → This is very rare. Document it, and suppress where it happens.
- **[Trade-off] Changes made in helpers, and entities passed out, are missed.** → Accepted for precision.
