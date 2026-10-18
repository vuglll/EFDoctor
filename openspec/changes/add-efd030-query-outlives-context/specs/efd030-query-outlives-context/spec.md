## Purpose

Define local, high-confidence detection of a deferred EF Core query returned from inside the `using` scope that disposes its `DbContext`, which throws `ObjectDisposedException` when the caller enumerates it.

## ADDED Requirements

### Requirement: Detect a deferred query returned past its context's disposal
EFD030 SHALL report a `return` statement whose returned value is a deferred EF Core query, and whose query context is disposed when the containing function returns.

The returned value is a deferred EF Core query when either of these holds:
- after implicit conversions, its type implements `System.Linq.IQueryable`;
- it is a resolved `AsEnumerable()` or `AsAsyncEnumerable()` call over such a query.

In both cases, the query SHALL be proven to originate at a `DbSet<T>` or `DbContext.Set<TEntity>()`, as defined by `query-chain-local-tracking`, including through query locals and self-composed query locals.

The query's context is the instance its `DbSet` origin is read from. It is disposed when the function returns, if it is a local declared by a `using` declaration or a `using` statement, synchronous or asynchronous, and the `return` is inside that `using` scope and belongs to the same method, lambda, or local function as the `using`. It is also disposed if it is a local initialized by `GetRequiredService<T>()` or `GetService<T>()` on the `ServiceProvider` of a local `IServiceScope` that is declared that way.

EFD030 SHALL anchor the finding on the returned expression.

#### Scenario: Using declaration
- **WHEN** a method declares `using var db = factory.CreateDbContext();` and returns `db.Orders.Where(...)`
- **THEN** EFD030 reports the returned expression

#### Scenario: Using statement
- **WHEN** a method returns `db.Orders` from inside `using (var db = new ShopContext()) { ... }`
- **THEN** EFD030 reports the returned expression

#### Scenario: Await using in an async method
- **WHEN** an async method declares `await using var db = ...` and returns a query over it as `Task<IQueryable<T>>`
- **THEN** EFD030 reports the returned expression

#### Scenario: Context resolved from a disposed service scope
- **WHEN** a method declares `using var scope = provider.CreateScope();`, resolves `var db = scope.ServiceProvider.GetRequiredService<ShopContext>();`, and returns a query over `db`
- **THEN** EFD030 reports the returned expression

#### Scenario: Query held in a local or enumerated lazily
- **WHEN** the method stores the query in a local, possibly recomposed in branches, and returns the local, or returns `query.AsEnumerable()`
- **THEN** EFD030 reports the returned expression

#### Scenario: Materialized result
- **WHEN** the method returns `db.Orders.ToList()`, `await db.Orders.ToListAsync()`, or any other materialized value
- **THEN** EFD030 does not report

#### Scenario: Context that outlives the method
- **WHEN** the query's context is an injected field, a parameter, or a local created without `using`
- **THEN** EFD030 does not report

#### Scenario: Return outside the using scope or in a nested function
- **WHEN** the `return` comes after a `using` statement's block, or is inside a lambda or local function declared within the `using` scope
- **THEN** EFD030 does not report

#### Scenario: Iterator method
- **WHEN** the method is an iterator that uses `yield return` inside the `using` scope
- **THEN** EFD030 does not report

### Requirement: Produce actionable EFD030 findings
Each EFD030 finding SHALL have:
- rule title `Query returned after its DbContext is disposed`;
- `Reliability` category, `Warning` severity, and high confidence;
- documentation key `EFD030`;
- the message `This query runs after its DbContext '{context}' is disposed`.

Evidence SHALL name the context local, how it is disposed, and the proven origin. Impact SHALL explain that the caller's enumeration throws `ObjectDisposedException`. Remediation SHALL recommend two fixes:
- materialize the result before returning;
- let the caller own the context's lifetime, for example by injecting a scoped context.

#### Scenario: Complete structured finding
- **WHEN** EFD030 reports a return
- **THEN** the finding has the title, category, severity, confidence, documentation key, message, evidence, impact, and remediation described above

### Requirement: Support standard suppression and existing reporting behavior
EFD030 SHALL use standard Roslyn suppression, and flow through the existing CLI, console and JSON output, deterministic ordering, and exit codes.

#### Scenario: Suppression
- **WHEN** a matching return is covered by `#pragma warning disable EFD030`, `[SuppressMessage("Reliability", "EFD030")]`, or `dotnet_diagnostic.EFD030.severity = none`
- **THEN** the finding is excluded

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan of the EFD030 fixture project runs in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD030 findings, with exact locations, JSON schema version `1`, and exit code `1`
