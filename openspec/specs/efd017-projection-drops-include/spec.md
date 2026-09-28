# EFD017 Projection Drops Include Specification

## Purpose

Defines high-confidence detection and reporting for EF Core include chains that become ineffective at a later projection boundary, while preserving legitimate entity-returning query shapes.

## Requirements

### Requirement: Detect includes made ineffective by projection
EFD017 SHALL report a high-confidence finding when a semantically resolved EF Core `Include` or `ThenInclude` occurs in a proven EF query chain before a semantically resolved `Queryable.Select`, and the selector projects only scalar, value, or newly constructed non-entity results rather than preserving an entity result on which the include can take effect.

#### Scenario: Scalar projection after include
- **WHEN** a proven `DbSet` query applies `Include` and then selects a scalar property from the source entity
- **THEN** EFD017 reports that the include does not populate navigation data in the projected result

#### Scenario: DTO projection after include
- **WHEN** a proven `DbSet` query applies `Include` or `ThenInclude` and then constructs a DTO from scalar or value-shaped expressions
- **THEN** EFD017 reports one finding for the projection boundary and identifies the preceding include path or paths

#### Scenario: Query composition between include and projection
- **WHEN** supported query operators such as filtering, ordering, query mode, or tagging occur between a proven include chain and an otherwise eligible projection
- **THEN** EFD017 still reports the ineffective include chain

### Requirement: Preserve entity-bearing projections
EFD017 SHALL NOT report when the selector preserves the source entity as the direct result or as a value within a constructed result. The first version SHALL also remain silent when a projected value directly preserves a source-rooted reference or collection whose entity semantics cannot be proven without EF model analysis.

#### Scenario: Identity projection
- **WHEN** a query with an include uses `Select(entity => entity)`
- **THEN** EFD017 does not report

#### Scenario: Wrapper containing the source entity
- **WHEN** a query with an include projects a wrapper whose member is the source entity itself
- **THEN** EFD017 does not report

#### Scenario: Direct source-rooted reference projection
- **WHEN** a selector directly returns or stores a source-rooted reference or collection value that may itself be an entity-bearing result
- **THEN** EFD017 does not report because the initial rule does not attempt EF model classification

### Requirement: Resolve query operations semantically
EFD017 SHALL identify EF Core include methods, LINQ projection, and the EF query origin by resolved symbols rather than method-name text. It SHALL support reduced extension syntax and equivalent static extension invocation for chains that remain directly connected.

#### Scenario: Proven DbSet origin
- **WHEN** an eligible chain originates at a `DbSet<T>` property or `DbContext.Set<T>()`
- **THEN** EFD017 treats the chain as an EF query candidate

#### Scenario: Unrelated same-named methods
- **WHEN** application code invokes non-EF methods named `Include` or `ThenInclude`, or a non-LINQ method named `Select`
- **THEN** EFD017 does not report

#### Scenario: Static extension syntax
- **WHEN** an eligible EF include and LINQ projection are expressed with static extension-method calls
- **THEN** EFD017 reports the same finding as for reduced extension syntax

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

### Requirement: Produce actionable and qualified findings
Each EFD017 finding SHALL use warning severity and high confidence, identify the resolved projection and preceding include path or paths, provide precise source coordinates at the `Select` projection boundary, explain that projected queries shape their own data and do not use the preceding include to populate omitted navigations, and recommend either removing the redundant include and explicitly projecting required data or returning the entity when populated navigation state is required.

#### Scenario: Finding contract
- **WHEN** EFD017 reports an ineffective include chain
- **THEN** the finding contains non-empty evidence, qualified impact, practical remediation, precise projection coordinates, and documentation key `EFD017`

#### Scenario: Projection already selects required navigation data
- **WHEN** the selector explicitly projects scalar data through an included navigation
- **THEN** remediation explains that the projection itself requests that data and the redundant include can be removed

### Requirement: Support standard suppression and generated-code exclusion
EFD017 SHALL honor standard Roslyn suppression and SHALL exclude generated code consistently with the other source analyzers.

#### Scenario: Suppressed finding
- **WHEN** an otherwise matching projection is suppressed by pragma, analyzer configuration, or `SuppressMessage`
- **THEN** EFD017 emits no finding for that projection

#### Scenario: Generated source
- **WHEN** an otherwise matching projection appears in generated source
- **THEN** EFD017 emits no finding

### Requirement: Validate precision with representative fixtures
EFD017 SHALL have at least ten positive and ten negative analyzer fixtures covering scalar and DTO projections, multiple and nested include paths, intervening supported composition, static and reduced invocation forms, entity-bearing projections, direct reference or collection projections, local boundaries, materialization boundaries, unrelated methods, generated code, exact locations, diagnostic properties, and suppression.

#### Scenario: Curated validation suite
- **WHEN** the EFD017 analyzer fixture suite runs
- **THEN** every curated ineffective-include case reports and every curated preserved, ambiguous, in-memory, or unrelated case remains clean
