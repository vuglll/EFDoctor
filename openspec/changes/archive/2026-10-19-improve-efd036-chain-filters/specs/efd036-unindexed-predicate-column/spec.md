## MODIFIED Requirements

### Requirement: Detect a predicate whose filter columns have no index
EFD036 SHALL consider every predicate lambda that EFD009 and EFD023 analyze: the predicate of a `Queryable` or EF Core async predicate operator over a proven EF Core query, including through query locals and self-composed query locals, whose parameter is the query's entity type.

Its filter columns SHALL be the properties `P` that appear in its top-level `&&` terms in one of these forms:
- `x.P == v`, or `v == x.P`;
- `x.P < v`, `x.P <= v`, `x.P > v`, or `x.P >= v`, either way round;
- `x.P.StartsWith(v)` on a `string` property;
- `c.Contains(x.P)`, where `c` is a local, parameter, or field collection.

Here `x` is the lambda's parameter, read directly. `P` is a simple mapped, non-`bool` property. `v` reads no lambda parameter.

The predicates traced to the same query origin are one query, and its filter columns are those of all its predicates. EFD036 SHALL report the query, once, when all of these hold:
- the compilation contains a model snapshot whose `BuildModel` maps the entity type;
- that entity's index and key coverage is conclusive;
- no filter column is the leading column of a `HasIndex`, `HasKey`, or `HasAlternateKey` property list of the entity or of any of its base types.

It SHALL anchor the finding on the first filter column's property access in the query's first predicate in source order, and report at compilation end.

#### Scenario: Equality on an unindexed column
- **WHEN** the snapshot maps `Order` with a key on `Id` and no other index, and a query filters `Where(o => o.Email == email)`
- **THEN** EFD036 reports `o.Email`

#### Scenario: Range, prefix, and membership filters
- **WHEN** the filter is `o.CreatedUtc >= since`, `o.Email.StartsWith(prefix)`, or `ids.Contains(o.CustomerNumber)` on an unindexed column
- **THEN** EFD036 reports the column

#### Scenario: Predicate terminal and stored query
- **WHEN** the filter is the predicate of `FirstOrDefaultAsync(o => o.Email == email)`, or of a `Where` over a query local
- **THEN** EFD036 reports the column

#### Scenario: Leading column of an index or key
- **WHEN** the column is the primary key, or leads a `HasIndex` or `HasAlternateKey` list of the entity or a base type
- **THEN** EFD036 does not report

#### Scenario: Another filter column is covered
- **WHEN** the predicate is `o.TenantId == tenant && o.Email == email`, and an index leads with `TenantId`
- **THEN** EFD036 does not report

#### Scenario: Indexed filter in another predicate of the query
- **WHEN** a query filters `Where(o => o.TenantId == tenant).Where(o => o.Email == email)`, or `Where(o => o.Id == id).AnyAsync(o => o.CreatedUtc >= since)`, and an index or key leads with `TenantId` or `Id`
- **THEN** EFD036 does not report

#### Scenario: Several unindexed predicates in one query
- **WHEN** a query filters `Where(o => o.Email == email).Where(o => o.CreatedUtc > since)`, and neither column leads an index
- **THEN** EFD036 reports once, at `o.Email`

#### Scenario: Separate queries stay separate
- **WHEN** one method runs a query filtered on an indexed column and another query on the same set filtered only on an unindexed one
- **THEN** EFD036 reports the second query

#### Scenario: Second column of a composite index
- **WHEN** the only index on the entity is `HasIndex("TenantId", "Email")`, and the predicate filters only on `Email`
- **THEN** EFD036 reports `o.Email`

#### Scenario: Outside the analyzable shapes
- **WHEN** the filter column is a `bool`, a navigation path such as `o.Customer.Email`, inside an `||`, compared with another column, or transformed such as `o.Email.ToLower()`
- **THEN** EFD036 does not report that term

#### Scenario: No snapshot or inconclusive coverage
- **WHEN** the compilation has no model snapshot that maps the entity, or the snapshot's coverage for it can't be read
- **THEN** EFD036 does not report
