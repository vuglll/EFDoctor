## MODIFIED Requirements

### Requirement: Resolve query operations semantically
EFD023 SHALL identify the predicate operator, the search method, and the query origin by resolved symbols, not by method-name text. It SHALL support reduced extension syntax and the equivalent static extension call.

#### Scenario: Unproven or in-memory source
- **WHEN** the same predicate is applied to an arbitrary `IQueryable<T>` parameter, a stored `IQueryable` local whose value is not statically determined, an `AsQueryable()` in-memory source, or an `IEnumerable<T>`
- **THEN** EFD023 does not report

#### Scenario: Unrelated same-named methods
- **WHEN** application code calls a non-`string` method named `Contains` or `EndsWith`, or a `Like` method that is not EF Core's
- **THEN** EFD023 does not report

#### Scenario: Static extension syntax
- **WHEN** an eligible predicate is passed through a static `Queryable.Where(...)` call
- **THEN** EFD023 reports the same finding as for reduced extension syntax

#### Scenario: Query stored in a followable local
- **WHEN** an eligible predicate is applied to a query local whose value is statically determined as defined by `query-chain-local-tracking`, and that value is a proven EF query
- **THEN** EFD023 evaluates the predicate as it would for the equivalent inline query
