# efd031-composed-stored-procedure Specification

## Purpose
Define high-confidence detection of LINQ composed over a stored-procedure call passed to `FromSqlRaw`, `FromSql`, or `FromSqlInterpolated`, which EF Core cannot compose and which throws at runtime.

## Requirements

### Requirement: Detect composition over a stored-procedure call
EFD031 SHALL report a composing query operator whose source reaches a resolved `RelationalQueryableExtensions.FromSqlRaw`, `FromSql`, or `FromSqlInterpolated` invocation whose SQL text begins, after leading whitespace and ignoring case, with the keyword `EXEC`, `EXECUTE`, or `CALL`.

The SQL text is one of these:
- the constant value of the SQL argument;
- for an interpolated string, its leading literal part.

The composing operators are:
- every resolved `System.Linq.Queryable` method except `AsQueryable`;
- EF Core `Include` and `ThenInclude`;
- every EF Core async terminal except `ToListAsync`, `ToArrayAsync`, `ToDictionaryAsync`, `ToHashSetAsync`, `ForEachAsync`, `LoadAsync`, and `AsAsyncEnumerable`.

Between the `FromSql` call and the operator, the source may pass only through `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `IgnoreQueryFilters`, `IgnoreAutoIncludes`, `TagWith`, and `TagWithCallSite`, and through query locals whose value is statically determined, as defined by `query-chain-local-tracking`. EFD031 SHALL report only the first composing operator, and anchor the finding on it.

#### Scenario: Where over EXEC
- **WHEN** `db.Orders.FromSqlRaw("EXEC dbo.GetOrders @p0", id).Where(o => o.Open).ToList()`
- **THEN** EFD031 reports the `Where` invocation

#### Scenario: Terminal that composes
- **WHEN** `db.Orders.FromSql($"EXECUTE dbo.GetOrder {id}").FirstOrDefault()` or `.CountAsync()`
- **THEN** EFD031 reports the terminal

#### Scenario: Include and pass-through operators
- **WHEN** `db.Orders.FromSqlRaw("exec GetOrders").AsNoTracking().Include(o => o.Lines)`
- **THEN** EFD031 reports the `Include` invocation

#### Scenario: CALL syntax and a query local
- **WHEN** `var orders = db.Orders.FromSqlRaw("CALL get_orders()");` is followed by `orders.OrderBy(o => o.Id).ToList()`
- **THEN** EFD031 reports the `OrderBy` invocation

#### Scenario: Direct materialization
- **WHEN** the result is materialized directly with `ToList()`, `ToListAsync()`, `AsEnumerable()`, or `foreach`, or composed only after `AsEnumerable()`
- **THEN** EFD031 does not report

#### Scenario: Composable SQL
- **WHEN** the SQL is `SELECT * FROM dbo.GetOrdersFn(@p0)`, or any text that doesn't begin with `EXEC`, `EXECUTE`, or `CALL`
- **THEN** EFD031 does not report

#### Scenario: Unknown SQL text
- **WHEN** the SQL argument is a parameter, a field, or a method result
- **THEN** EFD031 does not report

### Requirement: Produce actionable EFD031 findings
Each EFD031 finding SHALL have:
- rule title `LINQ composed over a stored-procedure call`;
- `Correctness` category, `Warning` severity, and high confidence;
- documentation key `EFD031`;
- the message `EF Core cannot compose {operator} over the stored-procedure call in {fromSql}`.

Evidence SHALL name the operator, the `FromSql` method, and the SQL's leading keyword. Impact SHALL explain that EF Core throws because it cannot wrap a stored-procedure call in a subquery. Remediation SHALL recommend two fixes:
- materialize with `AsEnumerable()` or `ToList()` before composing;
- use a composable table-valued function or view instead.

#### Scenario: Complete structured finding
- **WHEN** EFD031 reports an operator
- **THEN** the finding has the title, category, severity, confidence, documentation key, message, evidence, impact, and remediation described above

### Requirement: Support standard suppression and existing reporting behavior
EFD031 SHALL use standard Roslyn suppression, and flow through the existing CLI, console and JSON output, and exit codes.

#### Scenario: Suppression
- **WHEN** a matching operator is covered by `#pragma warning disable EFD031`, `[SuppressMessage("Correctness", "EFD031")]`, or `dotnet_diagnostic.EFD031.severity = none`
- **THEN** the finding is excluded

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan of the EFD031 fixture project runs in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD031 findings, with exact locations, JSON schema version `1`, and exit code `1`
