## MODIFIED Requirements

### Requirement: Bound the first version to direct query flow
EFD025 SHALL compare include chains only within one directly connected invocation chain that starts at a proven EF query origin. The chain may pass through element-preserving query composition, parentheses, and implicit conversions. EFD025 SHALL combine include state across a query local whose value is statically determined, as defined by `query-chain-local-tracking`, and report each finding once. It SHALL NOT combine include state across other local variables, fields, properties, parameters, returns, helper methods, custom query operators, element-type-changing operators, or materialization boundaries. It SHALL NOT consider auto-includes configured in the EF model.

#### Scenario: Include chain split across a local
- **WHEN** a query with `Include(b => b.Posts)` is assigned to a local whose value is statically determined, and a later statement applies `Include(b => b.Posts)` to that local
- **THEN** EFD025 reports the later include once, as a duplicate

#### Scenario: Include chain split across a conditionally reassigned local
- **WHEN** the same includes are separated by a local that is reassigned inside an `if` statement
- **THEN** EFD025 does not report

#### Scenario: Element-type-changing operator in the chain
- **WHEN** a projection, grouping, join, or other element-type-changing operator occurs between include chains, or between an include and the query origin
- **THEN** EFD025 does not compare include chains across that operator

#### Scenario: Include of an auto-included navigation
- **WHEN** a query includes a navigation that the EF model marks for automatic inclusion
- **THEN** EFD025 does not report, because model configuration is outside the first version
