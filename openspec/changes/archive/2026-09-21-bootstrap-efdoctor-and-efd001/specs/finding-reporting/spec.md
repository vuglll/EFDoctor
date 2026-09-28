# Spec Delta

## Purpose

Defines a stable, actionable finding contract and deterministic human-readable and machine-readable representations for EFDoctor scan results.

## ADDED Requirements

### Requirement: Findings expose a complete structured contract
Each finding SHALL contain a rule ID, rule title, severity, confidence, message, source file, one-based start line and column, one-based end line and column, evidence, likely performance impact, suggested remediation, and documentation reference or documentation key.

#### Scenario: Complete EFD001 finding
- **WHEN** EFD001 produces a finding
- **THEN** every required field is populated with a non-empty value except where an explicitly documented nullable representation is part of the JSON schema

#### Scenario: Source range conversion
- **WHEN** analyzer coordinates are converted into a finding
- **THEN** the finding exposes an accurate one-based start and end range for the matched invocation

### Requirement: EFD001 findings use trustworthy language
EFD001 findings SHALL use high confidence and SHALL explain repeated database round trips and reduced batching opportunity as likely performance impacts. Messages MUST NOT state or imply that every occurrence is necessarily wrong or that actual runtime cost has been measured.

#### Scenario: Finding describes likely impact
- **WHEN** an EFD001 finding is rendered
- **THEN** its confidence, impact, evidence, and remediation distinguish detected code structure from unmeasured runtime consequences

### Requirement: Findings have deterministic ordering
Before rendering, findings SHALL be ordered by normalized source-file path using ordinal comparison, then start line, start column, end line, end column, and rule ID. Repeated scans of unchanged inputs on the same machine SHALL produce the same finding order.

#### Scenario: Input diagnostics arrive in different orders
- **WHEN** equivalent findings are supplied to reporting in different discovery orders
- **THEN** console and JSON reports list them in the same deterministic order

#### Scenario: Multiple findings share a location
- **WHEN** findings share the same source range
- **THEN** rule ID provides a deterministic final ordering key

### Requirement: Console output is actionable
Human-readable output SHALL identify each finding's rule, severity, confidence, source range, message, evidence, likely impact, remediation, and documentation reference. Output SHALL include a summary count and SHALL remain readable without color.

#### Scenario: Console report contains findings
- **WHEN** a scan produces findings in console mode
- **THEN** the output presents every required finding field and a finding count in deterministic order

#### Scenario: Console report has no findings
- **WHEN** a scan completes successfully with no findings
- **THEN** the output clearly reports that no findings were found

### Requirement: JSON output is versioned and machine-readable
JSON output SHALL be valid JSON with no ANSI control sequences and SHALL include a top-level schema version, scan summary, and deterministically ordered findings using stable field names. The initial schema version SHALL be `1`.

#### Scenario: JSON report contains findings
- **WHEN** JSON output is requested for a successful scan
- **THEN** a parser can read schema version `1`, the summary, and every field in each finding without interpreting console text

#### Scenario: JSON report has no findings
- **WHEN** JSON output is requested and the scan has no findings
- **THEN** the report contains schema version `1`, a zero-finding summary, and an empty findings array
