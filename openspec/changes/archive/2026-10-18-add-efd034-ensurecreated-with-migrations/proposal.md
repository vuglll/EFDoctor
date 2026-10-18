# Proposal

## Why

`Database.EnsureCreated()` creates the schema straight from the current model, and it never records a migration. In a project that uses migrations, the two conflict:
- if `EnsureCreated` runs first, the database has no `__EFMigrationsHistory`, so a later `Migrate()` tries to create tables that already exist, and fails;
- if the database already exists, `EnsureCreated` does nothing, so new migrations are silently never applied by the code that was meant to set the database up.

EF Core's documentation warns against mixing them. The roadmap rates the rule cheap, with very low false-positive risk.

## What Changes

- Add **EFD034, `EnsureCreated` on a context that uses migrations**: `Correctness`, `Warning`, high confidence. It reports a call to `DatabaseFacade.EnsureCreated()` or `EnsureCreatedAsync()` on `<context>.Database`, when the compilation shows that the same context type uses migrations:
  - a `Migration` or `ModelSnapshot` subclass is attributed `[DbContext(typeof(T))]`, with `T` the context type or a base of it;
  - or the compilation calls `Database.Migrate()` or `MigrateAsync()` on a context of the same type.
- **Not reported:**
  - `EnsureCreated` on a context type that has no migrations in the compilation, such as a SQLite cache context next to a migrated main context;
  - `EnsureDeleted` alone;
  - test projects, as for every rule.

## Capabilities

### New Capabilities

- `efd034-ensurecreated-with-migrations`: detection, boundaries, finding contract, suppression, and tests for EFD034.

### Modified Capabilities

None.

## Impact

- **Code:** a new `EnsureCreatedWithMigrationsAnalyzer`, with registration in `WorkspaceAnalyzer` and the release file.
- **Tests:**
  - analyzer fixtures;
  - `tests/Fixtures/EFD034.Sample` with end-to-end tests;
  - an `EFD005.TestSample` case.
- **Docs:** `docs/rules/EFD034.md`, the rules README, the README, `PACKAGE.md`, usage, suppression, the roadmap, the development history, and the CHANGELOG.
- **Validation:** a corpus re-run, with every finding triaged.
