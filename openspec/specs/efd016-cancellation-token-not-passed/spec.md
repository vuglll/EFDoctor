# efd016-cancellation-token-not-passed Specification

## Purpose
Define advisory detection of an EF Core async operation that does not receive the `CancellationToken` available in its scope, so cancelling the request or shutting down the host doesn't cancel the database command.

## Requirements

### Requirement: Detect an EF Core async call that ignores a token in scope
EFD016 SHALL report a resolved EF Core asynchronous operation when both of these hold:
- **Missing token:** the invocation's `System.Threading.CancellationToken` parameter is bound to its default value implicitly, with no argument written. Alternatively, the operation is `DbSet<TEntity>.FindAsync(params object[])`, which takes no token.
- **Token in scope:** a parameter of type `CancellationToken` is in scope: a parameter of the containing method, or of an enclosing lambda or local function.

The EF Core asynchronous operations are:
- the methods whose names end in `Async` on `EntityFrameworkQueryableExtensions` or `RelationalQueryableExtensions`;
- `DbContext.SaveChangesAsync` and its overrides;
- `DbSet<TEntity>.FindAsync`.

EFD016 SHALL anchor the finding on the invocation, and name the token in the message.

#### Scenario: Query terminal without the token
- **WHEN** a method with a `CancellationToken cancellationToken` parameter calls `db.Orders.ToListAsync()`
- **THEN** EFD016 reports the `ToListAsync()` invocation and names `cancellationToken`

#### Scenario: SaveChanges, FindAsync, and bulk operations
- **WHEN** such a method calls `db.SaveChangesAsync()`, `db.Orders.FindAsync(id)`, or `db.Orders.Where(...).ExecuteDeleteAsync()`
- **THEN** EFD016 reports the invocation

#### Scenario: Token from an enclosing lambda
- **WHEN** a lambda with a `CancellationToken ct` parameter, such as a minimal API handler, calls an EF Core async operation without it
- **THEN** EFD016 reports the invocation and names `ct`

#### Scenario: Token passed or explicitly declined
- **WHEN** the call passes the token, `CancellationToken.None`, or `default`
- **THEN** EFD016 does not report

#### Scenario: No token in scope
- **WHEN** the containing method and its enclosing functions have no `CancellationToken` parameter
- **THEN** EFD016 does not report

### Requirement: Produce advisory EFD016 findings
Each EFD016 finding SHALL have:
- rule title `CancellationToken not passed to an EF Core async call`;
- `Reliability` category, `Info` severity, and advisory confidence;
- documentation key `EFD016`;
- the message `'{method}' doesn't pass the CancellationToken '{token}' that is in scope`.

Evidence SHALL name the operation and the token. Impact SHALL explain that the command keeps running after cancellation. Remediation SHALL show passing the token, including the `FindAsync(new object[] { key }, token)` form.

#### Scenario: Complete structured finding
- **WHEN** EFD016 reports an invocation
- **THEN** the finding has the title, category, severity, confidence, documentation key, message, evidence, impact, and remediation described above

### Requirement: Support standard suppression and existing reporting behavior
EFD016 SHALL use standard Roslyn suppression, and flow through the existing CLI, console and JSON output, and exit codes.

#### Scenario: Suppression
- **WHEN** a matching invocation is covered by `#pragma warning disable EFD016`, `[SuppressMessage("Reliability", "EFD016")]`, or `dotnet_diagnostic.EFD016.severity = none`
- **THEN** the finding is excluded

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan of the EFD016 fixture project runs in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD016 findings, with exact locations, JSON schema version `1`, and exit code `1`
