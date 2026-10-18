# efd034-ensurecreated-with-migrations Specification

## Purpose
Define high-confidence detection of `Database.EnsureCreated()` on a `DbContext` type that the same project manages with migrations, which bypasses the migration history.

## Requirements

### Requirement: Detect EnsureCreated on a migrated context
EFD034 SHALL report an invocation of `Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.EnsureCreated` or `EnsureCreatedAsync` whose receiver is the `Database` property of an expression whose type derives from `DbContext`. It SHALL report only when that context type uses migrations in the same compilation.

The context type uses migrations when either of these holds:
- a class derived from `Microsoft.EntityFrameworkCore.Migrations.Migration` or `Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot` is attributed `[DbContext(typeof(T))]`, where the context type is `T` or derives from `T`;
- the compilation invokes `Migrate` or `MigrateAsync` on the `Database` property of an expression of the same context type.

EFD034 SHALL NOT report an `EnsureCreated` call that is in one arm of a conditional, an `if`/`else` statement or a conditional expression, when the other arm calls `Migrate` or `MigrateAsync` on a context of the same type. Such a provider switch chooses one or the other on purpose. The conditional must be in the same method, lambda, or local function as the call.

EFD034 SHALL anchor the finding on the `EnsureCreated` invocation.

#### Scenario: EnsureCreated next to migrations
- **WHEN** a project contains a migration attributed `[DbContext(typeof(ShopContext))]` and calls `context.Database.EnsureCreated()` on a `ShopContext`
- **THEN** EFD034 reports the call

#### Scenario: Async form and model snapshot
- **WHEN** the project contains only a `ModelSnapshot` for `ShopContext`, and calls `await db.Database.EnsureCreatedAsync()`
- **THEN** EFD034 reports the call

#### Scenario: Migrate called elsewhere
- **WHEN** the project has no migration classes, but calls `db.Database.Migrate()` on a `ShopContext` in one place and `EnsureCreated()` on a `ShopContext` in another
- **THEN** EFD034 reports the `EnsureCreated` call

#### Scenario: Context without migrations
- **WHEN** the migrations belong to `ShopContext`, and `EnsureCreated` is called on an unrelated `CacheContext`
- **THEN** EFD034 does not report

#### Scenario: Project without migrations
- **WHEN** the compilation has no migration classes and no `Migrate` call
- **THEN** EFD034 does not report `EnsureCreated`

#### Scenario: Provider switch
- **WHEN** a method calls `await db.Database.EnsureCreatedAsync()` when `db.Database.IsSqlite()`, and `await db.Database.MigrateAsync()` in the `else` branch
- **THEN** EFD034 does not report the `EnsureCreated` call

#### Scenario: Unrelated EnsureCreated method
- **WHEN** application code calls a method named `EnsureCreated` that isn't EF Core's `DatabaseFacade` method
- **THEN** EFD034 does not report

### Requirement: Produce actionable EFD034 findings
Each EFD034 finding SHALL have:
- rule title `EnsureCreated on a context that uses migrations`;
- `Correctness` category, `Warning` severity, and high confidence;
- documentation key `EFD034`;
- the message `EnsureCreated bypasses the migrations of '{context}'`.

Evidence SHALL name the context type and the migration evidence: a migration or snapshot class, or a `Migrate` call. Impact SHALL explain that `EnsureCreated` creates no migration history, so a later `Migrate` fails, and that against an existing database it does nothing, so new migrations are never applied. Remediation SHALL recommend `Database.Migrate()`/`MigrateAsync()`, or applying migrations at deployment, and keeping `EnsureCreated` for contexts without migrations, such as tests.

#### Scenario: Complete structured finding
- **WHEN** EFD034 reports a call
- **THEN** the finding has the title, category, severity, confidence, documentation key, message, evidence, impact, and remediation described above

### Requirement: Support standard suppression and existing reporting behavior
EFD034 SHALL use standard Roslyn suppression, and flow through the existing CLI, console and JSON output, and exit codes.

#### Scenario: Suppression
- **WHEN** a matching call is covered by `#pragma warning disable EFD034`, `[SuppressMessage("Correctness", "EFD034")]`, or `dotnet_diagnostic.EFD034.severity = none`
- **THEN** the finding is excluded

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan of the EFD034 fixture project runs in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD034 findings, with exact locations, JSON schema version `1`, and exit code `1`
