# EFD025 Redundant Include Specification

## Purpose

Defines high-confidence detection and reporting for EF Core include paths that a query repeats exactly, or that a longer path in the same query already loads. Required branching syntax and include shapes whose meaning is uncertain are left alone.

## Requirements

### Requirement: Detect duplicate include paths
EFD025 SHALL report when a proven EF Core query chain contains two include chains with the same complete navigation path. An include chain is one semantically resolved `Include` plus any `ThenInclude` calls that directly continue it. The later chain in source order SHALL be reported as redundant.

#### Scenario: Same navigation included twice
- **WHEN** a proven `DbSet` query applies `Include(b => b.Posts)` twice
- **THEN** EFD025 reports the second include chain as a duplicate of the first

#### Scenario: Same multi-level path included twice
- **WHEN** a proven query contains `Include(b => b.Posts).ThenInclude(p => p.Author)` twice
- **THEN** EFD025 reports the second include chain as a duplicate of the first

#### Scenario: Duplicate separated by supported composition
- **WHEN** supported element-preserving operators such as filtering, ordering, paging, tracking mode, query-split mode, or tagging occur between two identical include chains
- **THEN** EFD025 still reports the later chain

#### Scenario: Three identical include chains
- **WHEN** a proven query contains the same include chain three times
- **THEN** EFD025 reports the second and third chains, one finding each

### Requirement: Detect include paths covered by a longer path
EFD025 SHALL report an include chain whose complete navigation path is a strict prefix of the complete path of another include chain in the same proven query. EF Core loads every navigation along the longer path, so the shorter chain adds nothing. This applies whether the covering chain comes before or after the covered chain.

#### Scenario: Bare include followed by a deeper path
- **WHEN** a proven query applies `Include(b => b.Posts)` and later `Include(b => b.Posts).ThenInclude(p => p.Author)`
- **THEN** EFD025 reports the bare `Include(b => b.Posts)` as covered by `Posts.Author`

#### Scenario: Deeper path precedes the bare include
- **WHEN** a proven query applies `Include(b => b.Posts).ThenInclude(p => p.Author)` and later `Include(b => b.Posts)`
- **THEN** EFD025 reports the later bare `Include(b => b.Posts)` as covered by `Posts.Author`

#### Scenario: Nested member path covers its prefix
- **WHEN** a proven query applies `Include(o => o.Customer)` and `Include(o => o.Customer.Address)`
- **THEN** EFD025 reports `Include(o => o.Customer)` as covered by `Customer.Address`

### Requirement: Preserve required branching and distinct paths
EFD025 SHALL NOT report include chains whose complete paths are distinct and not covered by one another. This SHALL hold even when the chains share a leading `Include` that is syntactically required to branch into different `ThenInclude` continuations.

#### Scenario: Sibling ThenInclude branches
- **WHEN** a proven query applies `Include(b => b.Posts).ThenInclude(p => p.Author)` and `Include(b => b.Posts).ThenInclude(p => p.Tags)`
- **THEN** EFD025 does not report

#### Scenario: Distinct sibling navigations
- **WHEN** a proven query includes `Posts` and `Owner` once each
- **THEN** EFD025 does not report

#### Scenario: Same property name on different declaring types
- **WHEN** two include paths use property names that match as text but resolve to different properties
- **THEN** EFD025 does not treat the paths as equal

### Requirement: Exclude include shapes whose equivalence cannot be proven
EFD025 SHALL NOT use an include chain in any comparison if any of its segments is a filtered include, a cast or type-test to a derived type, or a selector shape other than a plain property path rooted at the lambda parameter. A string include path SHALL be compared only with other string include paths. A string include SHALL be used only when its value is a compile-time constant, and it SHALL be compared using ordinal matching of its dot-separated segments.

#### Scenario: Filtered include alongside an unfiltered include
- **WHEN** a proven query applies `Include(b => b.Posts.Where(p => p.IsPublished))` and `Include(b => b.Posts)`
- **THEN** EFD025 does not report

#### Scenario: Two identical filtered includes
- **WHEN** a proven query applies the same filtered include twice
- **THEN** EFD025 does not report

#### Scenario: Include through a derived-type cast
- **WHEN** a proven query uses an include selector that casts the parameter to a derived type
- **THEN** that include chain takes part in no EFD025 comparison

#### Scenario: Duplicate constant string include
- **WHEN** a proven query applies `Include("Posts.Author")` twice
- **THEN** EFD025 reports the second string include as a duplicate

#### Scenario: String prefix covered by a longer string path
- **WHEN** a proven query applies `Include("Posts")` and `Include("Posts.Author")`
- **THEN** EFD025 reports `Include("Posts")` as covered by `Posts.Author`

#### Scenario: String and expression paths naming the same navigation
- **WHEN** a proven query applies `Include("Posts")` and `Include(b => b.Posts)`
- **THEN** EFD025 does not report

#### Scenario: Non-constant string include
- **WHEN** an include string is built from a variable, parameter, or non-constant expression
- **THEN** that include takes part in no EFD025 comparison

### Requirement: Resolve query operations semantically
EFD025 SHALL identify EF Core `Include` and `ThenInclude` and the EF query origin by resolved symbols, not by method-name text. It SHALL support reduced extension syntax and the equivalent static extension call for chains that stay directly connected.

#### Scenario: Proven DbSet origin
- **WHEN** an eligible chain originates at a `DbSet<T>` property or `DbContext.Set<T>()`
- **THEN** EFD025 treats the chain as an EF query candidate

#### Scenario: Unrelated same-named methods
- **WHEN** application code calls non-EF methods named `Include` or `ThenInclude` with repeated arguments
- **THEN** EFD025 does not report

#### Scenario: Static extension syntax
- **WHEN** an eligible redundant include is expressed with static extension-method calls
- **THEN** EFD025 reports the same finding as for reduced extension syntax

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

### Requirement: Produce actionable and qualified findings
Each EFD025 finding SHALL use `Info` severity, high confidence, and a maintainability category. It SHALL give precise source coordinates. These start at the method name of the redundant `Include`, or at the whole call for static extension syntax, and end at the end of that include chain's final `ThenInclude`. Evidence SHALL name the query origin, the redundant path, the path that duplicates or covers it, and whether the redundancy is a duplicate or a covered prefix. The finding SHALL explain that EF Core merges include paths, so the redundant chain changes neither the SQL nor the loaded data. It SHALL recommend deleting the redundant chain.

#### Scenario: Finding contract
- **WHEN** EFD025 reports a redundant include chain
- **THEN** the finding contains non-empty evidence, qualified impact, practical remediation, precise include-chain coordinates, and documentation key `EFD025`

#### Scenario: Redundant chain that is both a duplicate and a covered prefix
- **WHEN** a single include chain both duplicates an earlier chain and is covered by a longer chain
- **THEN** EFD025 reports it exactly once

### Requirement: Support standard suppression and generated-code exclusion
EFD025 SHALL honor standard Roslyn suppression and SHALL exclude generated code, as the other source analyzers do.

#### Scenario: Suppressed finding
- **WHEN** an otherwise matching redundant include is suppressed by pragma, analyzer configuration, or `SuppressMessage`
- **THEN** EFD025 emits no finding for that include

#### Scenario: Generated source
- **WHEN** an otherwise matching redundant include appears in generated source
- **THEN** EFD025 emits no finding

### Requirement: Validate precision with representative fixtures
EFD025 SHALL have at least ten positive and ten negative analyzer fixtures. Together they SHALL cover:

- exact duplicates, multi-level duplicates, and covered prefixes in either order
- nested member paths and constant string paths
- intervening supported composition, and static and reduced call forms
- sibling `ThenInclude` branching, distinct navigations, and same-named properties on different types
- filtered, cast, string-versus-expression, and non-constant string includes
- local and element-type-changing boundaries, unrelated methods, and generated code
- exact locations, diagnostic properties, and suppression

#### Scenario: Curated validation suite
- **WHEN** the EFD025 analyzer fixture suite runs
- **THEN** every curated redundant case reports exactly the expected number of findings and every curated distinct, branching, excluded, or unrelated case stays clean
