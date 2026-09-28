# Spec Delta

## Purpose

Define local, high-confidence detection of an EF Core `DbContext` held in a `static` (process-lifetime) field, which is not thread-safe and outlives the intended unit of work.

## ADDED Requirements

### Requirement: Detect a static DbContext field
EFD021 SHALL report a field declaration whose type is, or derives from, `Microsoft.EntityFrameworkCore.DbContext` and that is declared `static`. Type matching SHALL use resolved type symbols and base-type resolution rather than name text. EFD021 SHALL NOT report non-static (instance) fields, `const` fields, compiler-synthesized fields, or fields whose type is not a `DbContext`. Detecting a `DbContext` held by a dependency-injection singleton without a `static` field is out of scope for this version.

#### Scenario: Static DbContext field
- **WHEN** a type declares `private static AppDbContext _context;` where `AppDbContext` derives from `DbContext`
- **THEN** EFD021 reports the field declaration

#### Scenario: Static field of the base DbContext type
- **WHEN** a type declares a `static` field typed exactly as `DbContext`
- **THEN** EFD021 reports the field declaration

#### Scenario: Instance field is not reported
- **WHEN** a type declares a non-static `DbContext` field (for example injected and stored per instance)
- **THEN** EFD021 does not report

#### Scenario: Non-DbContext static field is not reported
- **WHEN** a `static` field's type does not derive from `DbContext`
- **THEN** EFD021 does not report

#### Scenario: Unresolved or unrelated type
- **WHEN** a field's type cannot be resolved, or a same-named type does not derive from EF Core `DbContext`
- **THEN** EFD021 does not report

### Requirement: Produce actionable and qualified EFD021 findings
Each EFD021 finding SHALL have rule title `DbContext is held in a static field`, high confidence, and documentation key `EFD021`, populate the shared finding contract, and span the field declaration using one-based coordinates. Evidence SHALL identify the field and its resolved `DbContext`-derived type. Impact language SHALL explain that a `DbContext` is not thread-safe and is intended to be short-lived, so static state causes concurrency errors, stale cached entities, and unbounded change-tracker growth, without claiming a specific runtime failure. Remediation SHALL recommend resolving a `DbContext` per unit of work — a scoped dependency-injection lifetime, a `DbContextFactory`, or a `using` block — and SHALL acknowledge that a provably single-threaded, short-lived usage can be suppressed with a recorded reason.

#### Scenario: Complete structured finding
- **WHEN** EFD021 reports a static DbContext field
- **THEN** console and JSON output contain every shared finding field with documentation key `EFD021`

#### Scenario: Exact diagnostic location
- **WHEN** EFD021 reports a field
- **THEN** the finding range identifies the field declaration using one-based coordinates

#### Scenario: Qualified remediation
- **WHEN** EFD021 reports a finding
- **THEN** remediation recommends a per-unit-of-work DbContext and names a provably single-threaded, short-lived usage as a legitimate suppressible case

### Requirement: Support standard suppression and existing reporting behavior
EFD021 SHALL use standard Roslyn diagnostic suppression (pragma, editor configuration, and `SuppressMessage` where supported) and flow through the existing local-only CLI, deterministic ordering, de-duplication, and stable exit codes without a new command, schema version, network activity, or telemetry.

#### Scenario: Pragma suppression
- **WHEN** a matching field is covered by `#pragma warning disable EFD021`
- **THEN** the analyzer result consumed by the CLI excludes that finding until warning restoration

#### Scenario: Editor configuration suppression
- **WHEN** applicable editor configuration sets `dotnet_diagnostic.EFD021.severity = none`
- **THEN** the analyzer result consumed by the CLI excludes EFD021 for that scope

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan produces EFD021 findings
- **THEN** both output formats contain the complete finding in deterministic order and retain the successful-scan-with-findings exit code and JSON schema version `1`

### Requirement: Validate EFD021 with focused and end-to-end tests
The change SHALL include analyzer fixtures covering positive cases (a `static` field of a derived `DbContext` type, a `static` field of the base `DbContext` type) and negative cases (an instance `DbContext` field, a non-`DbContext` static field, an unrelated same-named type, and standard suppression), and SHALL include at least one CLI end-to-end test that verifies console and JSON output for an EFD021 fixture project.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** positive and negative EFD021 fixtures verify detection, the instance/const exclusions, type resolution, and suppression

#### Scenario: CLI end-to-end fixture
- **WHEN** the CLI analyzes the EFD021 fixture project in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD021 findings with exact locations and JSON schema version `1`
