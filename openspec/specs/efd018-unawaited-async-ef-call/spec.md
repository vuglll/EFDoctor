# EFD018 Unawaited Async EF Call Specification

## Purpose

Defines high-confidence detection and reporting for EF Core asynchronous operations whose returned task is discarded instead of being awaited or otherwise observed.

## Requirements

### Requirement: Detect discarded EF Core asynchronous operation tasks
EFD018 SHALL report a high-confidence finding when the task returned by a semantically resolved EF Core asynchronous operation is directly discarded, either because the operation is the complete expression of an expression statement or because it is the value of an explicit discard assignment. A task wrapped only in `ConfigureAwait(...)` or reached through null-conditional access SHALL be treated as the same discarded task.

#### Scenario: Unawaited save in an async method
- **WHEN** an asynchronous method invokes `SaveChangesAsync` on an EF Core context as a standalone statement without awaiting it
- **THEN** EFD018 reports that the EF Core task is discarded

#### Scenario: Unawaited operation in a synchronous method
- **WHEN** a synchronous method invokes a supported EF Core asynchronous operation as a standalone statement
- **THEN** EFD018 reports the discarded task even though the compiler does not warn

#### Scenario: Explicit discard assignment
- **WHEN** a supported EF Core asynchronous operation is assigned to the discard `_`
- **THEN** EFD018 reports the discarded task

#### Scenario: Configured awaiter discarded
- **WHEN** a supported EF Core asynchronous operation is followed only by `ConfigureAwait(...)` and the result is discarded
- **THEN** EFD018 reports the discarded task

#### Scenario: Void-returning lambda discards the task
- **WHEN** an expression-bodied lambda converted to a void-returning delegate has a supported EF Core asynchronous operation as its body
- **THEN** EFD018 reports the discarded task

### Requirement: Recognize supported EF Core asynchronous operations
EFD018 SHALL identify EF Core asynchronous operations by resolved symbols rather than method-name text. The supported set SHALL include asynchronous operations declared by EF Core's queryable extensions (including query terminals, `ExecuteUpdateAsync`, and `ExecuteDeleteAsync`), `DbContext.SaveChangesAsync` including overrides, `DbSet<T>.FindAsync`, `AddAsync` and `AddRangeAsync` on `DbContext` and `DbSet<T>`, and relational `Database.ExecuteSql*Async` operations when the relational assembly is referenced. It SHALL support reduced extension syntax, equivalent static extension invocation, and derived context types.

#### Scenario: Query terminal discarded
- **WHEN** `ToListAsync`, `FirstAsync`, `AnyAsync`, or another supported EF Core query terminal is discarded
- **THEN** EFD018 reports the discarded task

#### Scenario: Bulk or raw-SQL operation discarded
- **WHEN** `ExecuteDeleteAsync`, `ExecuteUpdateAsync`, or a relational `ExecuteSqlRawAsync` operation is discarded
- **THEN** EFD018 reports the discarded task

#### Scenario: Unrelated same-named methods
- **WHEN** application code discards the task of a non-EF method named `SaveChangesAsync`, `ToListAsync`, `AddAsync`, or similar
- **THEN** EFD018 does not report

### Requirement: Exclude observed tasks
EFD018 SHALL NOT report when an EF Core task is awaited, returned, assigned to a local, field, property, or other non-discard target, passed as an argument, used as the receiver of any task API other than `ConfigureAwait`, or synchronously blocked by `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`, which EFD011 covers.

#### Scenario: Awaited operation
- **WHEN** a supported EF Core asynchronous operation is awaited, including after `ConfigureAwait(false)`
- **THEN** EFD018 does not report

#### Scenario: Returned or stored task
- **WHEN** a supported EF Core task is returned, assigned to a local or member, or produced by an expression-bodied lambda converted to a task-returning delegate
- **THEN** EFD018 does not report

#### Scenario: Task passed or composed
- **WHEN** a supported EF Core task is passed to `Task.WhenAll`, another method, or a continuation API such as `ContinueWith`
- **THEN** EFD018 does not report because the task is handed to code that may observe it

#### Scenario: Synchronously blocked task
- **WHEN** a supported EF Core task is consumed by `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`
- **THEN** EFD018 does not report and leaves the finding to EFD011

### Requirement: Produce actionable and qualified findings
Each EFD018 finding SHALL use warning severity and high confidence and provide precise source coordinates covering the complete discarded expression. It SHALL name the resolved EF Core operation and the discard form (statement, discard assignment, or void-returning lambda) as evidence, and SHALL explain that the operation may not complete before the context is reused or disposed, that its exceptions go unobserved, and that overlapping operations on the same context can fail or lose data. It SHALL recommend awaiting the operation and propagating async, and, for intentional background work, running it on a separately scoped context whose task is observed.

#### Scenario: Finding contract
- **WHEN** EFD018 reports a discarded EF Core task
- **THEN** the finding contains non-empty evidence, qualified impact, practical remediation, precise coordinates, and documentation key `EFD018`

#### Scenario: Multiple discarded operations
- **WHEN** one method discards several supported EF Core tasks
- **THEN** EFD018 reports one finding per discarded expression

### Requirement: Support standard suppression and generated-code exclusion
EFD018 SHALL honor standard Roslyn suppression and SHALL exclude generated code consistently with the other source analyzers.

#### Scenario: Suppressed finding
- **WHEN** an otherwise matching discard is suppressed by pragma, analyzer configuration, or `SuppressMessage`
- **THEN** EFD018 emits no finding for that discard

#### Scenario: Generated source
- **WHEN** an otherwise matching discard appears in generated source
- **THEN** EFD018 emits no finding

### Requirement: Validate precision with representative fixtures
EFD018 SHALL have at least ten positive and ten negative analyzer fixtures covering statement and discard-assignment forms, synchronous and asynchronous containing methods, `ConfigureAwait`, void-returning lambdas, query terminals, save, find, add, bulk and raw-SQL operations, static and reduced invocation forms, derived contexts, awaited, returned, stored, passed, composed, and blocked tasks, unrelated same-named methods, generated code, exact locations, diagnostic properties, and suppression.

#### Scenario: Curated validation suite
- **WHEN** the EFD018 analyzer fixture suite runs
- **THEN** every curated discarded-task case reports and every curated observed, blocked, or unrelated case remains clean
