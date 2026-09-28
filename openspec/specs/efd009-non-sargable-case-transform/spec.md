# EFD009 Non-Sargable Case Transform Specification

## Purpose

Defines medium-confidence detection and reporting for EF Core predicates that apply `ToLower()` or `ToUpper()` to a mapped column before comparing it. Wrapping the column in a function prevents an index seek on it. Transformed comparison values and shapes whose translation or sargability is uncertain are left alone.

## Requirements

### Requirement: Detect case-transformed columns in comparisons
EFD009 SHALL report when all of the following hold:

- A predicate lambda in a proven EF Core query applies the parameterless `string.ToLower()` or `string.ToUpper()` to a mapped string property path rooted at the lambda parameter.
- The transformed column is directly compared with a value that does not reference the lambda parameter.

Recognized comparisons are `==`, `!=`, static `string.Equals(a, b)`, instance `a.Equals(b)`, and `StartsWith(value)`. The operands may appear in either order.

#### Scenario: Equality against a lowered column
- **WHEN** a proven `DbSet` query uses `Where(customer => customer.Email.ToLower() == email)`
- **THEN** EFD009 reports the `ToLower()` applied to `Customer.Email`

#### Scenario: Reversed operand order
- **WHEN** a proven query uses `Where(customer => email == customer.Email.ToUpper())`
- **THEN** EFD009 reports the `ToUpper()` applied to `Customer.Email`

#### Scenario: Equals and StartsWith
- **WHEN** a proven query compares a lowered column with `string.Equals(customer.Email.ToLower(), email)`, `customer.Email.ToLower().Equals(email)`, or `customer.Email.ToLower().StartsWith(prefix)`
- **THEN** EFD009 reports each transformed column

#### Scenario: Reference navigation path
- **WHEN** a proven query uses `Where(order => order.Customer.Email.ToLower() == email)` and every segment is a mapped property
- **THEN** EFD009 reports the transformed `Customer.Email` path

#### Scenario: Several transformed columns in one predicate
- **WHEN** a predicate combines two transformed-column comparisons with `&&` or `||`
- **THEN** EFD009 reports each transformed column occurrence once

### Requirement: Recognize SQL-backed predicate positions
EFD009 SHALL inspect the predicate lambda passed to a proven `Queryable.Where`, and to the predicate overloads of `Queryable` `Any`, `All`, `Count`, `LongCount`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, and `LastOrDefault`. It SHALL also inspect the predicate overloads of the matching EF Core async terminals (`AnyAsync`, `AllAsync`, `CountAsync`, `LongCountAsync`, `FirstAsync`, `FirstOrDefaultAsync`, `SingleAsync`, `SingleOrDefaultAsync`, `LastAsync`, `LastOrDefaultAsync`).

#### Scenario: Terminal predicate overload
- **WHEN** a proven query calls `FirstOrDefaultAsync(customer => customer.Email.ToLower() == email)`
- **THEN** EFD009 reports the transformed column

#### Scenario: Predicate after supported composition
- **WHEN** filtering, ordering, tracking-mode, include, or tagging operators occur between the query origin and the predicate
- **THEN** EFD009 still evaluates the predicate

### Requirement: Preserve sargable and out-of-scope shapes
EFD009 SHALL NOT report in any of these cases:

- only the comparison value is transformed
- both operands reference the lambda parameter
- the transformed column is used with `Contains` or `EndsWith`, which are already non-sargable
- the transformation is `ToLowerInvariant`, `ToUpperInvariant`, or a culture-specific overload
- the property is computed or otherwise not a simple mapped property
- the transformation sits inside a nested lambda
- the predicate's parameter type differs from the query origin's entity type, for example after a `Select`

#### Scenario: Transformed comparison value
- **WHEN** a proven query uses `Where(customer => customer.Email == email.ToLower())`
- **THEN** EFD009 does not report

#### Scenario: Column-to-column comparison
- **WHEN** a proven query uses `Where(customer => customer.Email.ToLower() == customer.AlternateEmail.ToLower())`
- **THEN** EFD009 does not report

#### Scenario: Contains or EndsWith
- **WHEN** a proven query uses `customer.Email.ToLower().Contains(term)` or `customer.Email.ToLower().EndsWith(suffix)`
- **THEN** EFD009 does not report

#### Scenario: Invariant or culture overload
- **WHEN** a proven query uses `ToLowerInvariant()`, `ToUpperInvariant()`, or `ToLower(CultureInfo)` on a column
- **THEN** EFD009 does not report

#### Scenario: Computed property
- **WHEN** the transformed property has an expression-bodied or block-bodied getter
- **THEN** EFD009 does not report

#### Scenario: Nested lambda
- **WHEN** the transformation appears inside a nested lambda such as `customer.Tags.Any(tag => tag.Name.ToLower() == name)`
- **THEN** EFD009 does not report in the first version

#### Scenario: Predicate after projection
- **WHEN** the predicate follows a `Select` that changes the element type
- **THEN** EFD009 does not report

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

### Requirement: Produce actionable and qualified findings
Each EFD009 finding SHALL use warning severity, medium confidence, and the `Performance` category. Its location SHALL be the transformation call on the column. The finding SHALL contain:

- **Evidence:** the query origin, the column path, the transformation method, and the comparison.
- **Impact:** that applying the function to the column prevents an index seek on it, qualified as dependent on indexes, collation, and data.
- **Remediation:** compare the untransformed column; on a case-insensitive collation such as SQL Server's default, remove the transformation; otherwise use a case-insensitive collation or column type, `EF.Functions.ILike` on PostgreSQL, or normalize the stored value; and an existing expression index on the transformed column is a legitimate reason to suppress.

When the compilation references a supported relational provider, the remediation SHALL lead with that provider's option.

#### Scenario: Finding contract
- **WHEN** EFD009 reports a transformed column
- **THEN** the finding contains non-empty evidence, qualified impact, practical remediation, precise transformation coordinates, and documentation key `EFD009`

#### Scenario: SQL Server provider referenced
- **WHEN** the compilation references the SQL Server EF Core provider
- **THEN** the remediation leads with removing the transformation under a case-insensitive collation

#### Scenario: PostgreSQL provider referenced
- **WHEN** the compilation references the Npgsql EF Core provider
- **THEN** the remediation leads with a case-insensitive collation, `citext`, `EF.Functions.ILike`, or an expression index

### Requirement: Support standard suppression and generated-code exclusion
EFD009 SHALL honor standard Roslyn suppression and SHALL exclude generated code, as the other source analyzers do.

#### Scenario: Suppressed finding
- **WHEN** an otherwise matching transformation is suppressed by pragma, analyzer configuration, or `SuppressMessage`
- **THEN** EFD009 emits no finding for it

#### Scenario: Generated source
- **WHEN** an otherwise matching transformation appears in generated source
- **THEN** EFD009 emits no finding

### Requirement: Validate precision with representative fixtures
EFD009 SHALL have at least ten positive and ten negative analyzer fixtures. Together they SHALL cover:

- every recognized comparison and predicate position, both operand orders, navigation paths, and multiple occurrences
- static and reduced call forms
- every excluded shape, and unproven and in-memory sources
- unrelated methods and generated code
- provider-aware remediation, exact locations, diagnostic properties, and suppression

#### Scenario: Curated validation suite
- **WHEN** the EFD009 analyzer fixture suite runs
- **THEN** every curated transformed-column case reports exactly the expected number of findings and every curated sargable, excluded, or unrelated case stays clean
