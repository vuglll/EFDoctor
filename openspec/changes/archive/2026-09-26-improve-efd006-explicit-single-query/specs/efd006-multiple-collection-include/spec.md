## MODIFIED Requirements

### Requirement: Respect split-query and client boundaries
EFD006 SHALL NOT report a query chain that contains a resolved EF Core `AsSplitQuery` or `AsSingleQuery`. With `AsSplitQuery`, the sibling collections are loaded using separate SQL queries. With `AsSingleQuery`, the developer has explicitly chosen single-query loading, and EF Core itself does not warn about multiple collection includes in that case. EFD006 SHALL NOT cross `AsEnumerable`, `AsAsyncEnumerable`, or another explicit client-evaluation boundary, and the first version SHALL NOT follow query values through locals, fields, properties, parameters, return values, helper methods, or general data flow.

#### Scenario: Explicit split query
- **WHEN** an otherwise eligible query chain applies resolved EF Core `AsSplitQuery` before or after its sibling collection includes
- **THEN** EFD006 does not report

#### Scenario: Explicit single query
- **WHEN** an otherwise eligible sibling-collection chain applies resolved EF Core `AsSingleQuery` before or after its sibling collection includes
- **THEN** EFD006 does not report, because the single-query trade-off was chosen explicitly

#### Scenario: Stored query
- **WHEN** include calls and later query execution are separated through a local or member
- **THEN** EFD006 analyzes only each proven inline chain and does not infer composition across that boundary

#### Scenario: Explicit client boundary
- **WHEN** query composition crosses `AsEnumerable` or `AsAsyncEnumerable`
- **THEN** EFD006 does not infer a database include chain across that boundary
