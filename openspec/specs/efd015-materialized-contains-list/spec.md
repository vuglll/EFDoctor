# efd015-materialized-contains-list Specification

## Purpose
Define advisory detection of a key list materialized from one EF Core query only to filter another query on the same context with `Contains`, where composing a subquery would avoid the round trip and the unbounded parameter list.

## Requirements

### Requirement: Detect a materialized key list used only in another query's Contains
EFD015 SHALL report a resolved `Contains` invocation when all of these hold:
- **The lambda:** the invocation is inside a lambda, or a lambda nested in it. The lambda is an argument of a resolved `System.Linq.Queryable` operator whose source is proven to originate at an EF Core `DbSet`, including through followed and self-composed query locals.
- **The collection:** it is a read of a local, and the tested value reads a parameter of that lambda.
- **The local's value:** at the read, it is statically determined as defined by `query-chain-local-tracking`. It is a resolved `ToList`, `ToArray`, or `ToHashSet`, or the awaited `ToListAsync`, `ToArrayAsync`, or `ToHashSetAsync`, over a proven EF Core query whose recognized row bound is not strong: no `Take`, and no key equality.
- **The contexts:** both queries' `DbContext` instances have the same symbol key.
- **Other uses:** every reference to the local in the method is the collection of a `Contains` invocation.

EFD015 SHALL anchor the finding on the `Contains` invocation.

#### Scenario: Key list from an unbounded query
- **WHEN** `var ids = db.Customers.Where(c => c.Region == r).Select(c => c.Id).ToList();` is followed by `db.Orders.Where(o => ids.Contains(o.CustomerId))`
- **THEN** EFD015 reports `ids.Contains(o.CustomerId)`

#### Scenario: Async load and a HashSet
- **WHEN** the list is `await db.Customers.Select(c => c.Id).ToListAsync()` or `.ToHashSet()`, and the filter uses `Enumerable.Contains(ids, o.CustomerId)` or the instance `Contains`
- **THEN** EFD015 reports the `Contains` invocation

#### Scenario: Bounded or key-filtered inner query
- **WHEN** the inner query has `Take(...)`, or filters on a key with equality
- **THEN** EFD015 does not report

#### Scenario: List used for something else
- **WHEN** the method also reads `ids.Count`, enumerates `ids`, returns it, or passes it to a method
- **THEN** EFD015 does not report

#### Scenario: In-memory or passed-in list
- **WHEN** the collection is a parameter, a field, or a local built in memory
- **THEN** EFD015 does not report

#### Scenario: Different contexts
- **WHEN** the list is loaded from a different `DbContext` instance than the filtered query
- **THEN** EFD015 does not report

### Requirement: Produce advisory EFD015 findings
Each EFD015 finding SHALL have:
- rule title `Materialized key list used in another query's Contains`;
- `Performance` category, `Info` severity, and advisory confidence;
- documentation key `EFD015`;
- the message `'{local}' is loaded from another query only to filter this one; compose it as a subquery`.

Evidence SHALL name the local and its loading query. Impact SHALL explain the extra round trip and the unbounded list. Remediation SHALL recommend removing the materialization so EF Core composes `IN (SELECT …)`, or using a join.

#### Scenario: Complete structured finding
- **WHEN** EFD015 reports an invocation
- **THEN** the finding has the title, category, severity, confidence, documentation key, message, evidence, impact, and remediation described above

### Requirement: Support standard suppression and existing reporting behavior
EFD015 SHALL use standard Roslyn suppression, and flow through the existing CLI, console and JSON output, and exit codes.

#### Scenario: Suppression
- **WHEN** a matching invocation is covered by `#pragma warning disable EFD015`, `[SuppressMessage("Performance", "EFD015")]`, or `dotnet_diagnostic.EFD015.severity = none`
- **THEN** the finding is excluded

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan of the EFD015 fixture project runs in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD015 findings, with exact locations, JSON schema version `1`, and exit code `1`
