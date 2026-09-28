# Spec Delta

## Purpose

Defines high-confidence detection and reporting for dynamically constructed SQL passed to EF Core raw-SQL APIs, while preserving safe parameterized usage and avoiding name-based false positives.

## ADDED Requirements

### Requirement: Detect unsafe SQL passed to EF Core raw APIs
EFD012 SHALL report a high-confidence finding when SQL passed to a semantically resolved EF Core `FromSqlRaw`, `ExecuteSqlRaw`, or `ExecuteSqlRawAsync` operation is directly constructed with string interpolation or non-constant string concatenation.

#### Scenario: Direct interpolated raw query
- **WHEN** an interpolated string is passed directly as the SQL argument of `FromSqlRaw`
- **THEN** EFD012 reports the SQL expression as unsafe raw-SQL construction

#### Scenario: Direct concatenated raw command
- **WHEN** a string concatenation containing a non-constant value is passed directly as the SQL argument of `ExecuteSqlRaw` or `ExecuteSqlRawAsync`
- **THEN** EFD012 reports the SQL expression as unsafe raw-SQL construction

### Requirement: Follow proven local SQL origins
EFD012 SHALL follow local-variable assignments and initializers within the containing executable body when the SQL argument's origin can be uniquely proven to be interpolation or non-constant concatenation. It SHALL NOT report when reaching definitions are ambiguous, unavailable, or include a safe value.

#### Scenario: Local initialized from interpolation
- **WHEN** a local SQL variable is initialized from an interpolated string and then passed unchanged to a supported raw-SQL API
- **THEN** EFD012 reports the raw-SQL invocation with evidence identifying the unsafe local origin

#### Scenario: Local reassigned on multiple paths
- **WHEN** a local SQL variable can reach a supported raw-SQL API from both unsafe and safe assignments
- **THEN** EFD012 does not report because the unsafe origin is not uniquely proven

### Requirement: Resolve supported APIs semantically
EFD012 SHALL identify supported EF Core raw-SQL methods by resolved symbols rather than method-name text, including reduced extension and static invocation forms.

#### Scenario: Unrelated same-named method
- **WHEN** application code invokes a non-EF method named `FromSqlRaw`, `ExecuteSqlRaw`, or `ExecuteSqlRawAsync`
- **THEN** EFD012 does not report it

#### Scenario: Static extension invocation
- **WHEN** a supported EF Core raw-SQL extension is invoked in static form with unsafe SQL
- **THEN** EFD012 reports the same finding it would report for reduced extension syntax

### Requirement: Preserve safe SQL forms
EFD012 SHALL NOT report constant SQL, raw SQL using placeholders with values supplied as separate arguments, or SQL passed to EF Core interpolated-safe APIs such as `FromSqlInterpolated`, `FromSql`, `ExecuteSqlInterpolated`, and `ExecuteSqlInterpolatedAsync`.

#### Scenario: Raw SQL with separate parameters
- **WHEN** constant SQL containing parameter placeholders is passed to a supported raw API and values are supplied through its parameter arguments
- **THEN** EFD012 does not report

#### Scenario: Interpolated-safe API
- **WHEN** dynamic values are supplied through a semantically resolved EF Core interpolated-safe SQL API
- **THEN** EFD012 does not report

#### Scenario: Compile-time constant concatenation
- **WHEN** the SQL argument is a concatenation whose complete value is a compile-time constant
- **THEN** EFD012 does not report

### Requirement: Produce actionable and qualified findings
Each EFD012 finding SHALL identify the resolved EF Core API and unsafe construction form, use high confidence, describe SQL injection and query-plan-cache risk as possible consequences rather than established runtime harm, and recommend a parameterized or interpolated-safe EF Core API while acknowledging that identifiers and SQL structure require explicit allow-listing rather than value parameters.

#### Scenario: Finding contract
- **WHEN** EFD012 reports unsafe SQL construction
- **THEN** the finding contains precise source coordinates, non-empty evidence, qualified likely impact, practical remediation, and documentation key `EFD012`

### Requirement: Support standard suppression and generated-code exclusion
EFD012 SHALL honor standard Roslyn suppression and SHALL exclude generated code consistently with the other source analyzers.

#### Scenario: Suppressed finding
- **WHEN** an otherwise matching invocation is suppressed by pragma, analyzer configuration, or `SuppressMessage`
- **THEN** EFD012 emits no finding for that invocation

#### Scenario: Generated source
- **WHEN** an otherwise matching invocation appears in generated source
- **THEN** EFD012 emits no finding

### Requirement: Validate precision with representative fixtures
EFD012 SHALL have at least ten positive and ten negative analyzer fixtures covering every supported raw API, direct and local unsafe origins, static and reduced invocation forms, safe parameterization, interpolated-safe APIs, unrelated methods, ambiguity, generated code, exact locations, properties, and suppression.

#### Scenario: Curated validation suite
- **WHEN** the EFD012 analyzer fixture suite runs
- **THEN** every curated unsafe case reports and every curated safe or unrelated case remains clean
