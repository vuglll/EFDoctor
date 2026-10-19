# Proposal

## Why

The CLI treats a project as unanalyzable when its source has a `using Microsoft.EntityFrameworkCore` directive and its compilation can't resolve `DbContext`. An entity-model project can reference only `Microsoft.EntityFrameworkCore.Abstractions`, for mapping attributes such as `[Index]`, and declare no `DbContext`. In the round-2 validation corpus, Kavita's `Kavita.Models` is such a project. The CLI warned that its EF Core types could not be resolved and told the user to restore and build, though it was restored, and the corpus marked the run incomplete.

## What Changes

- A project that uses EF Core but can't resolve `DbContext` is unanalyzable only when its compilation references no `Microsoft.EntityFrameworkCore*` assembly. An unrestored project resolves none.
- A project that references the EF Core abstractions without EF Core itself is analyzed as usual, with no warning.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `cli-ef-resolution-check`: the unanalyzable-project definition, plus an entity-model scenario.

## Impact

- **Code:** `WorkspaceAnalyzer` in the CLI.
- **Tests:** an end-to-end test on a new `Workspace.EfAbstractionsOnly` fixture.
- **Docs:** the CHANGELOG.
