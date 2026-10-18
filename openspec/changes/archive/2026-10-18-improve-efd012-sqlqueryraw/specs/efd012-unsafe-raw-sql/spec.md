## MODIFIED Requirements

### Requirement: Detect unsafe SQL passed to EF Core raw APIs
EFD012 SHALL report a high-confidence finding when SQL passed to a semantically resolved EF Core `FromSqlRaw`, `ExecuteSqlRaw`, `ExecuteSqlRawAsync`, or `SqlQueryRaw` operation is directly constructed with string interpolation or non-constant string concatenation.

#### Scenario: Direct interpolated raw query
- **WHEN** an interpolated string is passed directly as the SQL argument of `FromSqlRaw`
- **THEN** EFD012 reports the SQL expression as unsafe raw-SQL construction

#### Scenario: Direct concatenated raw command
- **WHEN** a string concatenation containing a non-constant value is passed directly as the SQL argument of `ExecuteSqlRaw` or `ExecuteSqlRawAsync`
- **THEN** EFD012 reports the SQL expression as unsafe raw-SQL construction

#### Scenario: Interpolated SqlQueryRaw
- **WHEN** an interpolated string, or a local initialized from one, is passed as the SQL argument of `context.Database.SqlQueryRaw<int>(...)`
- **THEN** EFD012 reports the SQL expression as unsafe raw-SQL construction

### Requirement: Resolve supported APIs semantically
EFD012 SHALL identify supported EF Core raw-SQL methods by resolved symbols rather than method-name text, including reduced extension and static invocation forms.

#### Scenario: Unrelated same-named method
- **WHEN** application code invokes a non-EF method named `FromSqlRaw`, `ExecuteSqlRaw`, `ExecuteSqlRawAsync`, or `SqlQueryRaw`
- **THEN** EFD012 does not report it

#### Scenario: Static extension invocation
- **WHEN** a supported EF Core raw-SQL extension is invoked in static form with unsafe SQL
- **THEN** EFD012 reports the same finding it would report for reduced extension syntax

### Requirement: Preserve safe SQL forms
EFD012 SHALL NOT report constant SQL, raw SQL using placeholders with values supplied as separate arguments, or SQL passed to EF Core interpolated-safe APIs such as `FromSqlInterpolated`, `FromSql`, `ExecuteSqlInterpolated`, `ExecuteSqlInterpolatedAsync`, and `SqlQuery`.

#### Scenario: Raw SQL with separate parameters
- **WHEN** constant SQL containing parameter placeholders is passed to a supported raw API and values are supplied through its parameter arguments
- **THEN** EFD012 does not report

#### Scenario: Interpolated-safe API
- **WHEN** dynamic values are supplied through a semantically resolved EF Core interpolated-safe SQL API
- **THEN** EFD012 does not report

#### Scenario: Compile-time constant concatenation
- **WHEN** the SQL argument is a concatenation whose complete value is a compile-time constant
- **THEN** EFD012 does not report
