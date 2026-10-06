# Spec Delta

## MODIFIED Requirements

### Requirement: Severity may be derived from confidence
The diagnostic-to-finding mapping SHALL derive finding severity from the reported confidence when a rule does not fix its own severity. A finding whose confidence is advisory SHALL map to `Info` severity; a finding whose confidence is medium or high SHALL map to `Warning` severity. A medium-confidence finding SHALL map to `Warning` even though its diagnostic has `Info` severity in a build (see the `analyzer-package` capability). This mapping MUST NOT change the finding contract, the field set, or the JSON schema version, and MUST NOT alter the severity of rules that report a fixed confidence and severity.

#### Scenario: Advisory maps to Info
- **WHEN** a rule reports a finding with advisory confidence
- **THEN** the mapped finding has `Info` severity in both console and JSON output

#### Scenario: Medium and high map to Warning
- **WHEN** a rule reports a finding with medium or high confidence
- **THEN** the mapped finding has `Warning` severity

#### Scenario: Medium stays Warning when the build severity is Info
- **WHEN** a medium-confidence diagnostic has `Info` severity, as it does by default in a build
- **THEN** the mapped finding has `Warning` severity

#### Scenario: Rule with a fixed severity
- **WHEN** EFD025 reports a high-confidence finding at its fixed `Info` severity
- **THEN** the mapped finding has `Info` severity

#### Scenario: Schema version unchanged
- **WHEN** confidence-driven severity is applied
- **THEN** JSON output retains schema version `1` and every previously required finding field
