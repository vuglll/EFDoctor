## MODIFIED Requirements

### Requirement: Bound the first version to direct query flow
EFD017 SHALL follow directly connected invocation chains through supported query-composition operations, parentheses, and implicit conversions. It SHALL follow a query local whose value is statically determined, as defined by `query-chain-local-tracking`. It SHALL NOT infer include state through other local variables, fields, properties, parameters, returns, helper methods, custom query operators, or materialization boundaries.

#### Scenario: Inline direct chain
- **WHEN** an eligible include and projection remain connected in one invocation chain
- **THEN** EFD017 evaluates the chain

#### Scenario: Include chain stored in a local
- **WHEN** a query containing an include is assigned to a local whose value is statically determined, and a later statement projects that local to a non-entity shape
- **THEN** EFD017 reports the projection as it would for the equivalent inline chain

#### Scenario: Include chain stored in a conditionally reassigned local
- **WHEN** a query containing an include is assigned to a local that is reassigned inside an `if` statement, and a later statement projects that local
- **THEN** EFD017 does not report

#### Scenario: Materialization before projection
- **WHEN** a query is materialized before an in-memory `Enumerable.Select`
- **THEN** EFD017 does not report because the include was applied to the materialized entity query
