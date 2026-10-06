# Proposal

## Why

`ExecuteUpdate` and `ExecuteDelete` run directly in the database and bypass the change tracker. Entities of that type which the same `DbContext` already tracks keep their old values, a later `Find` returns the stale instance, and saving a stale entity can overwrite the bulk update or fail on a deleted row. The shape was suggested on the 0.3.0 announcement thread (issue #11). It is also the bug that a naive fix for EFD013 introduces.

## What Changes

- Add EFD038, which reports an `ExecuteUpdate`, `ExecuteDelete`, or their async forms when the same method has already loaded entities of the same type, with tracking, into a local from the same `DbContext` instance.
- Two confidence tiers: medium when the tracked entities are only left stale, and high when the method uses them after the bulk operation, loads the type again from the same context, or calls `SaveChanges`.
- Stay silent when the context is cleared, or an entity is reloaded or detached, after the load.
- Extract EFD027's proof that two operations use one context instance, so both rules share it.
- Reference EFD038 from EFD013's rule page.

## Capabilities

### New Capabilities

- `efd038-stale-tracked-entities`: detection and reporting of bulk operations that leave tracked entities of the same type stale on the same context.

### Modified Capabilities

None.

## Impact

Adds one analyzer, a shared context-identity helper that EFD027 now uses without behavior change, tests, a sample fixture, a case in the shared test-project fixture, and documentation entries. The finding contract, the JSON schema, and exit codes do not change.
