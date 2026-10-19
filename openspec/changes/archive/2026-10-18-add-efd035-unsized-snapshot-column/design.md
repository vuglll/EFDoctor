# Design

## Context

EFD003's `SnapshotCollector` already records each entity's index and key property lists from the snapshot. EFD035 adds a pass over the `Property(...)` chains of the same `BuildModel`.

## Goals / Non-Goals

**Goals:**
- a high-confidence finding for the migration-breaking indexed `nvarchar(max)`;
- an advisory finding for decimals left at the provider default.

**Non-Goals:**
- unindexed unbounded strings, which are too often intentional;
- providers other than SQL Server;
- reading fluent configuration in other projects.

## Decisions

### Read the fluent chain above `Property(...)`

**Decision:** For each `Property` invocation with a constant name, inside an entity builder (found with EFD003's `TryGetContainingEntity`), walk up the chain of invocations whose instance is the previous call. Record the `HasColumnType` argument, and whether `HasPrecision` appears.

**Why:** This is exactly how the snapshot generator writes property configuration.

### One rule, two confidence levels

**Decision:** One descriptor, `Correctness`/`Warning`, with `high` for indexed unbounded strings and `advisory` for decimals. `EfDiagnostic` turns advisory findings into `Info` suggestions in a build.

**Why:** Both are snapshot column-type hygiene. The indexed string always fails, while the decimal only might lose data. EFD005 and EFD038 already mix confidence levels in one rule.

### Decimal attribute exclusion through the CLR type

**Decision:** Resolve the snapshot entity name with `Compilation.GetTypeByMetadataName`. If the type resolves and its property has `[Precision]` or `[Column(TypeName = ...)]`, don't report.

**Why:** Attribute configuration is visible from any project. Fluent `HasColumnType("decimal(18,2)")` elsewhere is indistinguishable in the snapshot, so it's a documented reason to suppress.

## Risks / Trade-offs

- **[Risk] An explicitly configured `HasColumnType("decimal(18,2)")` is reported.** → The confidence is advisory, the suppression reason is documented, and the remediation says so.
