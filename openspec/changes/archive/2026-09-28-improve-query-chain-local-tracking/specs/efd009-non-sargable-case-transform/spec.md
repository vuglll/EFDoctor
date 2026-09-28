## MODIFIED Requirements

### Requirement: Resolve query operations semantically
EFD009 SHALL identify the predicate operator, the transformation method, and the query origin by resolved symbols, not by method-name text. It SHALL support reduced extension syntax and the equivalent static extension call.

#### Scenario: Proven DbSet origin
- **WHEN** an eligible predicate belongs to a chain that originates at a `DbSet<T>` property or `DbContext.Set<T>()`
- **THEN** EFD009 evaluates the predicate

#### Scenario: Unproven or in-memory source
- **WHEN** the same predicate is applied to an arbitrary `IQueryable<T>` parameter, a stored query local whose value is not statically determined, an `AsQueryable()` in-memory source, or an `IEnumerable<T>`
- **THEN** EFD009 does not report

#### Scenario: Unrelated same-named methods
- **WHEN** application code calls a non-`string` method named `ToLower` or `ToUpper`, or a non-LINQ method named `Where`
- **THEN** EFD009 does not report

#### Scenario: Static extension syntax
- **WHEN** an eligible predicate is passed through a static `Queryable.Where(...)` call
- **THEN** EFD009 reports the same finding as for reduced extension syntax

#### Scenario: Query stored in a followable local
- **WHEN** an eligible predicate is applied to a query local whose value is statically determined as defined by `query-chain-local-tracking`, and that value is a proven EF query
- **THEN** EFD009 evaluates the predicate as it would for the equivalent inline query
