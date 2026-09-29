# EFD027 Concurrent DbContext Operations Specification

## Purpose
Defines high-confidence detection and reporting of EF Core asynchronous operations that start while another operation on the same `DbContext` instance is provably still pending. At run time this makes EF Core throw because a second operation started on the context instance.

## Requirements

### Requirement: Recognize EF Core operations and their context instance
EFD027 SHALL consider the resolved EF Core asynchronous operations that EFD011 and EFD018 recognize:

- asynchronous `EntityFrameworkQueryableExtensions` methods;
- `DbContext.SaveChangesAsync`, including overrides;
- `DbSet<TEntity>.FindAsync`.

The *context* of an operation SHALL be resolved as follows:

- `SaveChangesAsync`: the context is its receiver.
- `FindAsync`: the context is the `DbContext` that owns the `DbSet` receiver.
- Queryable operation: the context is the `DbContext` that owns the query's proven `DbSet<TEntity>` or `DbContext.Set<TEntity>()` origin.

A context reference SHALL be comparable only when it is one of these:

- a local that is never written after its declaration;
- a value parameter that is never assigned;
- a field or auto-property accessed on `this` or statically;
- `this`.

Two operations SHALL share a context only when their comparable context references resolve to the same symbol. An operation whose context is not comparable SHALL NOT take part in any finding.

#### Scenario: Context through a DbSet property
- **WHEN** two operations run on `db.Orders` and `db.Customers` for the same never-reassigned local `db`
- **THEN** EFD027 treats both operations as sharing the context `db`

#### Scenario: Context through Set and SaveChangesAsync
- **WHEN** one operation queries `db.Set<Order>()` and another calls `db.SaveChangesAsync()` on the same parameter `db`
- **THEN** EFD027 treats both operations as sharing the context `db`

#### Scenario: Injected context field
- **WHEN** two operations use the field `_db` accessed on `this`
- **THEN** EFD027 treats both operations as sharing the context `_db`

#### Scenario: Different context instances
- **WHEN** two operations use two different locals, each created by `IDbContextFactory<TContext>.CreateDbContext()`
- **THEN** EFD027 does not report

#### Scenario: Context that cannot be compared
- **WHEN** an operation's context is a reassigned local, a property with a custom getter, a method call result, or a member of another object
- **THEN** EFD027 does not use that operation in any finding

### Requirement: Detect operations passed together to a task combinator
EFD027 SHALL report when two or more operations that share a context are passed directly as arguments to resolved `System.Threading.Tasks.Task.WhenAll` or `Task.WhenAny`, including through a `params` argument list, an array creation, or a collection expression. Every such operation after the first SHALL be reported, because arguments are evaluated in order and each starts while the earlier ones are pending.

#### Scenario: Two queries in Task.WhenAll
- **WHEN** code calls `Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync())`
- **THEN** EFD027 reports the second operation

#### Scenario: Three operations in Task.WhenAny
- **WHEN** code passes three operations on the same context to `Task.WhenAny`
- **THEN** EFD027 reports the second and the third operation, one finding each

#### Scenario: Array or collection expression argument
- **WHEN** the operations are passed as `new[] { ... }` or `[ ... ]` to `Task.WhenAll`
- **THEN** EFD027 reports as for a `params` argument list

#### Scenario: Different contexts in one combinator
- **WHEN** `Task.WhenAll` receives one operation on `db1` and one on `db2`
- **THEN** EFD027 does not report

### Requirement: Detect an operation started while a task local is unobserved
EFD027 SHALL report an operation that starts while a task local on the same context is still unobserved. The task local SHALL be declared directly in a block and initialized with an operation. The later operation SHALL share that operation's context and be in a later statement of the same block. No reference to the task local SHALL occur, in source order, between its declaration and the later operation. Operations inside lambdas and local functions SHALL NOT count as starting in that block.

#### Scenario: Two task locals awaited together
- **WHEN** code declares `var orders = db.Orders.ToListAsync();` then `var customers = db.Customers.ToListAsync();` and then awaits `Task.WhenAll(orders, customers)`
- **THEN** EFD027 reports the operation that initializes `customers`

#### Scenario: Task local awaited after a later operation
- **WHEN** code declares `var orders = db.Orders.ToListAsync();`, then awaits `db.Customers.CountAsync()`, then awaits `orders`
- **THEN** EFD027 reports the `CountAsync` operation

#### Scenario: Task local awaited before the next operation
- **WHEN** code declares `var orders = db.Orders.ToListAsync();`, awaits `orders`, and only then starts another operation on `db`
- **THEN** EFD027 does not report

#### Scenario: Task local referenced before the next operation
- **WHEN** the task local is passed to a method, stored, or inspected before the next operation on the same context
- **THEN** EFD027 does not report, because the reference may observe completion

#### Scenario: Later operation inside a lambda
- **WHEN** the only later operation on the same context is inside a lambda or local function
- **THEN** EFD027 does not report

### Requirement: Detect operations projected into a task combinator
EFD027 SHALL report an operation inside the selector of a resolved `System.Linq.Enumerable.Select` whose context is captured from outside the selector, when the `Select` result reaches `Task.WhenAll` or `Task.WhenAny`. It reaches the combinator when it is passed directly, through `ToList()` or `ToArray()`, or through a local whose value at the combinator is statically determined. Each selector invocation starts an operation on the same context while earlier ones are pending. The finding SHALL be reported once, at the operation inside the selector.

#### Scenario: Async selector over a captured context
- **WHEN** code calls `await Task.WhenAll(ids.Select(async id => await db.Orders.FindAsync(id)))`
- **THEN** EFD027 reports the `FindAsync` operation

#### Scenario: Selected tasks stored in a local
- **WHEN** code declares `var tasks = ids.Select(id => db.Orders.FirstAsync(o => o.Id == id)).ToList();` and then awaits `Task.WhenAll(tasks)`
- **THEN** EFD027 reports the `FirstAsync` operation

#### Scenario: Context created inside the selector
- **WHEN** the selector creates its own context, for example `await using var db = factory.CreateDbContext();`, before the operation
- **THEN** EFD027 does not report

### Requirement: Leave sequential and unproven code alone
EFD027 SHALL NOT report operations that are awaited before the next operation on the same context starts, operations on in-memory or non-EF sources, same-named methods that don't resolve to EF Core, `Task.WhenAll`/`Task.WhenAny` look-alikes, or generated code. It SHALL NOT throw on malformed code.

#### Scenario: Sequential awaits
- **WHEN** code awaits `db.Orders.ToListAsync()` and then awaits `db.Customers.ToListAsync()`
- **THEN** EFD027 does not report

#### Scenario: Unrelated asynchronous work in a combinator
- **WHEN** `Task.WhenAll` receives one EF operation and non-EF tasks such as an HTTP call
- **THEN** EFD027 does not report

#### Scenario: Look-alike methods
- **WHEN** a method named `WhenAll`, `Select`, or `ToListAsync` resolves to a type other than `Task`, `Enumerable`, or the EF Core extension classes
- **THEN** EFD027 does not report

#### Scenario: Generated or malformed code
- **WHEN** the shapes appear in generated code, or an operation doesn't resolve
- **THEN** EFD027 does not report and does not throw

### Requirement: Produce actionable and qualified EFD027 findings
Each EFD027 finding SHALL have:

- rule title `Concurrent operations on one DbContext`;
- category `Reliability`, `Warning` severity, and high confidence;
- documentation key `EFD027`;
- every field of the shared finding contract populated.

The finding SHALL span the invocation of the operation that starts while another is pending, using one-based coordinates. Evidence SHALL name the resolved operation, the shared context reference, the pending operation or task local, and the shape: combinator arguments, unobserved task local, or projected tasks. Impact SHALL explain that `DbContext` isn't thread-safe and that EF Core throws `InvalidOperationException` when a second operation starts before the first completes, depending on timing. Remediation SHALL recommend awaiting each operation before starting the next, and using a separate context per concurrent operation, for example through `IDbContextFactory<TContext>`, when concurrency is required. The confidence-driven severity mapping and the universal test-project downgrade apply as for other rules.

#### Scenario: Complete structured finding
- **WHEN** EFD027 reports an operation
- **THEN** console and JSON output contain every shared finding field with documentation key `EFD027`, high confidence, and `Warning` severity

#### Scenario: Exact diagnostic location
- **WHEN** EFD027 reports the second operation in `Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync())`
- **THEN** the range covers exactly `db.Customers.ToListAsync()`

#### Scenario: Qualified remediation
- **WHEN** EFD027 reports a finding
- **THEN** remediation names sequential awaiting and a context per concurrent operation through `IDbContextFactory<TContext>`

### Requirement: Support standard suppression and existing reporting behavior
EFD027 SHALL use standard Roslyn diagnostic suppression: pragma, editor configuration, and `SuppressMessage("Reliability", "EFD027")`. Findings SHALL flow through the existing local-only CLI, with its deterministic ordering, de-duplication, and stable exit codes. No new command, schema version, network activity, or telemetry is added.

#### Scenario: Pragma suppression
- **WHEN** a reported operation is covered by `#pragma warning disable EFD027`
- **THEN** the CLI result excludes that finding until the warning is restored

#### Scenario: Editor configuration suppression
- **WHEN** applicable editor configuration sets `dotnet_diagnostic.EFD027.severity = none`
- **THEN** the CLI result excludes EFD027 for that scope

#### Scenario: SuppressMessage attribute
- **WHEN** the containing member has `[SuppressMessage("Reliability", "EFD027")]`
- **THEN** the finding is excluded, and unsuppressed findings elsewhere still report

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan produces EFD027 findings
- **THEN** both output formats contain the complete finding in deterministic order, keep the successful-scan-with-findings exit code, and use JSON schema version `1`

### Requirement: Validate EFD027 with focused and end-to-end tests
The change SHALL include at least ten positive and ten negative analyzer fixtures that cover every scenario in this specification. It SHALL include at least one CLI end-to-end test that verifies console and JSON output for an EFD027 fixture project.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** the positive and negative EFD027 fixtures verify every shape, context-identity rule, exclusion, the exact span, and suppression, with no analyzer exceptions

#### Scenario: CLI end-to-end fixture
- **WHEN** the CLI analyzes the EFD027 fixture project in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD027 findings, with exact locations and JSON schema version `1`
