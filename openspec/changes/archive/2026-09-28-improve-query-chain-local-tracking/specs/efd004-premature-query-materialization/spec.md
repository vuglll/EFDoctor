## MODIFIED Requirements

### Requirement: Preserve intentional client-evaluation boundaries and expression-local scope
EFD004 SHALL NOT report when `AsEnumerable` or `AsAsyncEnumerable` establishes an explicit client-evaluation boundary before materialization. EFD004 SHALL analyze the materializer's containing fluent expression. It MAY trace the materializer's query source through a query local whose value is statically determined, as defined by `query-chain-local-tracking`. It SHALL NOT follow a materialized result through a local, field, property, return value, argument, helper method, or general data flow.

#### Scenario: Explicit AsEnumerable boundary
- **WHEN** an EF query explicitly crosses `AsEnumerable` before materialization and later applies a LINQ operator
- **THEN** EFD004 does not report

#### Scenario: Explicit AsAsyncEnumerable boundary
- **WHEN** an EF query explicitly crosses `AsAsyncEnumerable` before client-side asynchronous composition
- **THEN** EFD004 does not report

#### Scenario: Materialized local used later
- **WHEN** a supported materialization is assigned to a local and a later statement filters, projects, orders, or pages that local
- **THEN** EFD004 does not report in the first version

#### Scenario: Materialization is terminal
- **WHEN** an eligible EF query is materialized and no supported operator is composed afterward
- **THEN** EFD004 does not report

#### Scenario: Filtering occurs before materialization
- **WHEN** a supported filter, projection, ordering, or paging operator is part of the EF query before its terminal materialization and no supported operator follows materialization
- **THEN** EFD004 does not report

#### Scenario: Unsupported immediate operator precedes a supported one
- **WHEN** the immediate operation after materialization is unsupported or ineligible even though a later operation in the same expression is supported
- **THEN** EFD004 does not skip over the immediate operation to report the materialization

#### Scenario: Comments strings and inactive code
- **WHEN** relevant method names occur only in comments, strings, or inactive preprocessor code
- **THEN** EFD004 emits no diagnostic for that text

#### Scenario: Query source stored in a local
- **WHEN** an EF query is stored in a local whose value is statically determined, and a later statement materializes that local and immediately filters, projects, orders, or pages the result
- **THEN** EFD004 reports as it would for the equivalent inline query
