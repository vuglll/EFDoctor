# Proposal

## Why

Loading matching entities, changing or deleting each one in a loop, and then calling `SaveChanges` adds avoidable row transfer, change-tracking work, and per-row database commands where EF Core 7+ can issue a set-based `ExecuteUpdate` or `ExecuteDelete`. EFD013 is the next rule in the product brief's recommended order after EFD011 and EFD012, and a deliberately narrow structural match can provide useful modern-EF guidance without guessing about complex business logic.

## What Changes

- Add EFD013 detection for semantically proven EF Core queries that are materialized, iterated only to perform uniform mapped-property assignments or per-entity deletion, and followed by a matching `SaveChanges` or `SaveChangesAsync` call.
- Restrict the initial rule to same-block, single-write patterns whose query, loop variable, mutation/delete action, and save operation can be connected without interprocedural or path-sensitive assumptions.
- Avoid reports when the loop contains additional behavior, mutations depend on the entity's current values, save operations occur inside the loop, query tracking or control flow is ambiguous, supported bulk APIs are unavailable, or equivalence to a set-based operation cannot be established.
- Emit medium-confidence, actionable findings that identify the materialization, loop action, and save operation; explain likely transfer, tracking, and command-count costs; and warn that bulk operations bypass change tracking and have different concurrency, interceptor, and transaction semantics.
- Register EFD013 in CLI workspace analysis and add analyzer, fixture, suppression, console, JSON, documentation, and regression coverage.

## Capabilities

### New Capabilities

- `efd013-bulk-update-delete-analysis`: Defines conservative semantic detection, exclusions, evidence, remediation, suppression, and validation coverage for load/loop/save patterns replaceable by EF Core `ExecuteUpdate` or `ExecuteDelete`.

### Modified Capabilities

- `cli-analysis`: Requires EFD013 to run in the CLI analyzer set and flow through the existing reporting, ordering, suppression, privacy, test-project downgrade, and exit-code contracts.

## Impact

The change adds one Roslyn analyzer, shared operation-analysis support where useful, analyzer and CLI fixtures/tests, EFD013 rule documentation, CLI analyzer registration, release metadata, and README status updates. It uses the existing Roslyn and EF Core test dependencies, requires no product runtime dependency or network access, and does not change the versioned JSON schema or add an automatic code fix.
