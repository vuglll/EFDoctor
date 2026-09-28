# Spec Delta

## Purpose

Allow finding severity to be derived from confidence so that advisory findings render as informational, without changing the finding contract or the JSON schema version.

## ADDED Requirements

### Requirement: Severity may be derived from confidence
The diagnostic-to-finding mapping SHALL derive finding severity from the reported confidence when a rule does not fix its own severity. A finding whose confidence is advisory SHALL map to `Info` severity; a finding whose confidence is medium or high SHALL map to `Warning` severity. This mapping MUST NOT change the finding contract, the field set, or the JSON schema version, and MUST NOT alter the severity of rules that report a fixed confidence and severity.

#### Scenario: Advisory maps to Info
- **WHEN** a rule reports a finding with advisory confidence
- **THEN** the mapped finding has `Info` severity in both console and JSON output

#### Scenario: Medium and high map to Warning
- **WHEN** a rule reports a finding with medium or high confidence
- **THEN** the mapped finding has `Warning` severity

#### Scenario: Schema version unchanged
- **WHEN** confidence-driven severity is applied
- **THEN** JSON output retains schema version `1` and every previously required finding field
