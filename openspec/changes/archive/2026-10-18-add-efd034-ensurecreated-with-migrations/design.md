# Design

## Context

Migrations are classes deriving from `Migration`, with a `[DbContext(typeof(T))]` attribute and a matching `ModelSnapshot` class. Both are usually in the project that defines the context, or in a dedicated migrations project.

## Goals / Non-Goals

**Goals:** report `EnsureCreated` only on a context type the same compilation provably migrates.

**Non-Goals:**
- migrations in another assembly, unless the same compilation calls `Migrate` on that context;
- `EnsureDeleted`/`EnsureCreated` pairs in test projects, which the analysis scope already skips.

## Decisions

### Migration evidence from symbols, collected once per compilation

**Decision:**
1. At compilation start, register a symbol action for named types derived from `Migration` or `ModelSnapshot`. Read the type argument of their `DbContextAttribute`, and collect the migrated context types.
2. Register an operation action that collects the context types of `Migrate`/`MigrateAsync` calls, and the `EnsureCreated` candidates.
3. At compilation end, report each candidate whose context type is, or derives from, a migrated context type.

**Why:** Migration evidence can be anywhere in the compilation, after or before the call. A compilation-end action sees all of it without requesting other trees' semantic models, which RS1030 forbids.

**Alternative considered:** scanning the syntax trees for `[DbContext(typeof(...))]` by name. Rejected, because the symbols are available and exact.

### Category and confidence

`Correctness`, `Warning`, high. The conflict is deterministic: either `Migrate` fails later, or migrations are skipped.

## Risks / Trade-offs

- **[Risk] Compilation-end diagnostics surface only in full-solution analysis in some IDEs.** → The CLI and builds report them. That's documented.
- **[Trade-off] Migrations in a separate assembly with no `Migrate` call in this project are missed.** → Accepted.
