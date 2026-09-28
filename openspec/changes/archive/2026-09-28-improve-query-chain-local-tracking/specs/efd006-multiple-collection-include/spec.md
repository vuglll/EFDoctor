## MODIFIED Requirements

### Requirement: Respect split-query and client boundaries
EFD006 SHALL NOT report a query chain that contains a resolved EF Core `AsSplitQuery` or `AsSingleQuery`. With `AsSplitQuery`, the sibling collections are loaded using separate SQL queries. With `AsSingleQuery`, the developer has explicitly chosen single-query loading, and EF Core itself does not warn about multiple collection includes in that case. EFD006 SHALL NOT cross `AsEnumerable`, `AsAsyncEnumerable`, or another explicit client-evaluation boundary, and it SHALL NOT follow query values through fields, properties, parameters, return values, helper methods, general data flow, or locals whose value is not statically determined. It SHALL follow a query local whose value is statically determined, as defined by `query-chain-local-tracking`, and report each finding once.

#### Scenario: Explicit split query
- **WHEN** an otherwise eligible query chain applies resolved EF Core `AsSplitQuery` before or after its sibling collection includes
- **THEN** EFD006 does not report

#### Scenario: Explicit single query
- **WHEN** an otherwise eligible sibling-collection chain applies resolved EF Core `AsSingleQuery` before or after its sibling collection includes
- **THEN** EFD006 does not report, because the single-query trade-off was chosen explicitly

#### Scenario: Stored query
- **WHEN** sibling collection includes are separated by a query local whose value is statically determined, with no split mode on either side
- **THEN** EFD006 combines the includes across the local and reports once, at the second distinct collection include

#### Scenario: Stored query in a member
- **WHEN** include calls and later query execution are separated through a field or property, or a local reassigned conditionally
- **THEN** EFD006 analyzes only each proven chain and does not infer composition across that boundary

#### Scenario: Explicit client boundary
- **WHEN** query composition crosses `AsEnumerable` or `AsAsyncEnumerable`
- **THEN** EFD006 does not infer a database include chain across that boundary
