# EFD023 Leading-Wildcard Search Specification

## Purpose

Defines advisory detection and reporting for EF Core predicates that search a mapped column with a substring, suffix, or leading-wildcard pattern. These searches cannot seek an ordinary index. Prefix searches and shapes whose pattern or translation is uncertain are left alone.

## Requirements

### Requirement: Detect substring and suffix searches on a column
EFD023 SHALL report when both of the following hold:

- A predicate lambda in a proven EF Core query calls `string.Contains(string)` or `string.EndsWith(string)` on a mapped string property path rooted at the lambda parameter. The column may be used directly or wrapped in the parameterless `ToLower()` or `ToUpper()`.
- The searched value does not reference the lambda parameter.

#### Scenario: Substring search
- **WHEN** a proven `DbSet` query uses `Where(customer => customer.Name.Contains(term))`
- **THEN** EFD023 reports the `Contains` call on `Customer.Name`

#### Scenario: Suffix search
- **WHEN** a proven query uses `Where(customer => customer.Email.EndsWith(domain))`
- **THEN** EFD023 reports the `EndsWith` call on `Customer.Email`

#### Scenario: Case-transformed column
- **WHEN** a proven query uses `Where(customer => customer.Name.ToLower().Contains(term))`
- **THEN** EFD023 reports the `Contains` call once, and its evidence notes the case transformation

#### Scenario: Reference navigation path
- **WHEN** a proven query uses `Where(order => order.Customer.Name.Contains(term))` and every segment is a mapped property
- **THEN** EFD023 reports the search on `Order.Customer.Name`

#### Scenario: Several searches in one predicate
- **WHEN** a predicate combines two eligible searches with `||` or `&&`
- **THEN** EFD023 reports each search call once

### Requirement: Detect leading-wildcard LIKE patterns
EFD023 SHALL report `EF.Functions.Like(column, pattern)` and its escape-character overload when all of the following hold:

- The column argument is a mapped string property path rooted at the lambda parameter.
- The pattern provably starts with `%` or `_`.
- The pattern does not reference the lambda parameter.

A pattern provably starts with a wildcard when it is one of these:

- a constant string
- a string concatenation whose leftmost operand is such a constant
- an interpolated string whose first segment is literal text starting with `%` or `_`

#### Scenario: Constant leading wildcard
- **WHEN** a proven query uses `Where(customer => EF.Functions.Like(customer.Name, "%smith"))`
- **THEN** EFD023 reports the `Like` call

#### Scenario: Concatenated leading wildcard
- **WHEN** a proven query uses `EF.Functions.Like(customer.Name, "%" + term + "%")`
- **THEN** EFD023 reports the `Like` call

#### Scenario: Interpolated leading wildcard
- **WHEN** a proven query uses `EF.Functions.Like(customer.Name, $"%{term}")`
- **THEN** EFD023 reports the `Like` call

#### Scenario: Prefix pattern
- **WHEN** a proven query uses `EF.Functions.Like(customer.Name, term + "%")` or `EF.Functions.Like(customer.Name, "smith%")`
- **THEN** EFD023 does not report

#### Scenario: Pattern of unknown shape
- **WHEN** the pattern is a variable, parameter, or other expression whose leading character cannot be determined
- **THEN** EFD023 does not report

### Requirement: Recognize SQL-backed predicate positions
EFD023 SHALL inspect the predicate lambda passed to a proven `Queryable.Where`, and to the predicate overloads of `Queryable` `Any`, `All`, `Count`, `LongCount`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, and `LastOrDefault`. It SHALL also inspect the predicate overloads of the matching EF Core async terminals.

#### Scenario: Terminal predicate overload
- **WHEN** a proven query calls `AnyAsync(customer => customer.Name.Contains(term))`
- **THEN** EFD023 reports the search

#### Scenario: Predicate after supported composition
- **WHEN** filtering, ordering, tracking-mode, include, or tagging operators occur between the query origin and the predicate
- **THEN** EFD023 still evaluates the predicate

### Requirement: Preserve prefix searches and out-of-scope shapes
EFD023 SHALL NOT report in any of these cases:

- `StartsWith`
- `Contains` or `EndsWith` overloads that take `StringComparison` (EFD022 reports those) or a `char`
- a collection's `Contains(column)`
- a search whose value references the lambda parameter
- a search on a computed or unmapped property
- a search inside a nested lambda
- a predicate whose parameter type differs from the query origin's entity type

#### Scenario: Prefix search
- **WHEN** a proven query uses `Where(customer => customer.Name.StartsWith(prefix))`
- **THEN** EFD023 does not report

#### Scenario: StringComparison overload
- **WHEN** a proven query uses `customer.Name.Contains(term, StringComparison.OrdinalIgnoreCase)`
- **THEN** EFD023 does not report

#### Scenario: Collection membership
- **WHEN** a proven query uses `Where(customer => names.Contains(customer.Name))`
- **THEN** EFD023 does not report

#### Scenario: Column-to-column search
- **WHEN** a proven query uses `Where(customer => customer.Name.Contains(customer.Nickname))`
- **THEN** EFD023 does not report

#### Scenario: Nested lambda
- **WHEN** the search appears inside a nested lambda such as `customer.Tags.Any(tag => tag.Name.Contains(term))`
- **THEN** EFD023 does not report in the first version

#### Scenario: Computed property
- **WHEN** the searched property has an expression-bodied or block-bodied getter
- **THEN** EFD023 does not report

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

### Requirement: Produce advisory and actionable findings
Each EFD023 finding SHALL use `Info` severity, advisory confidence, and the `Performance` category. Its location SHALL be the search invocation. The finding SHALL contain:

- **Evidence:** the query origin, the column path, the search form (`Contains`, `EndsWith`, or `EF.Functions.Like`), any case transformation, and why the pattern has a leading wildcard.
- **Impact:** that a leading-wildcard search cannot seek an ordinary index and typically scans, qualified as acceptable for small or rarely searched tables.
- **Remediation:**
  - use a prefix search when the semantics allow it
  - use full-text search
  - for suffix searches, use a reversed or computed column
  - accept the scan for small tables

When the compilation references a supported relational provider, the remediation SHALL lead with that provider's full-text or trigram option.

#### Scenario: Finding contract
- **WHEN** EFD023 reports a search
- **THEN** the finding contains non-empty evidence, qualified impact, practical remediation, precise search coordinates, and documentation key `EFD023`

#### Scenario: SQL Server provider referenced
- **WHEN** the compilation references the SQL Server EF Core provider
- **THEN** the remediation leads with SQL Server full-text search through `EF.Functions.Contains` or `EF.Functions.FreeText`

#### Scenario: PostgreSQL provider referenced
- **WHEN** the compilation references the Npgsql EF Core provider
- **THEN** the remediation leads with a `pg_trgm` trigram index or `tsvector` full-text search

### Requirement: Support standard suppression and generated-code exclusion
EFD023 SHALL honor standard Roslyn suppression and SHALL exclude generated code, as the other source analyzers do.

#### Scenario: Suppressed finding
- **WHEN** an otherwise matching search is suppressed by pragma, analyzer configuration, or `SuppressMessage`
- **THEN** EFD023 emits no finding for it

#### Scenario: Generated source
- **WHEN** an otherwise matching search appears in generated source
- **THEN** EFD023 emits no finding

### Requirement: Validate precision with representative fixtures
EFD023 SHALL have at least ten positive and ten negative analyzer fixtures. Together they SHALL cover:

- every search form and every leading-wildcard pattern shape
- case-transformed columns, navigation paths, and multiple occurrences
- every predicate position, and static and reduced call forms
- every excluded shape, and unproven and in-memory sources
- unrelated methods and generated code
- provider-aware remediation, exact locations, diagnostic properties, and suppression

#### Scenario: Curated validation suite
- **WHEN** the EFD023 analyzer fixture suite runs
- **THEN** every curated search case reports exactly the expected number of findings and every curated prefix, excluded, or unrelated case stays clean
