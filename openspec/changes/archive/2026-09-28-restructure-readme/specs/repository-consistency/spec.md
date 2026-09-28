## MODIFIED Requirements

### Requirement: Register and document every shipped rule
For every diagnostic ID that the analyzer assembly exports, the repository SHALL contain all of the following:

- the rule's registration in the CLI analyzer set
- a rule capability spec under `openspec/specs/` whose folder name starts with the lowercase rule ID
- a rule page at `docs/rules/<ID>.md`
- a row in the README rule table
- coverage in the README project status line
- coverage in the analyzer-test list of the verification map in `docs/development.md`
- a row in the package readme rule table
- a release-tracking entry
- an entry in the CHANGELOG

A rule ID is covered by the status line or verification list when it is named directly or falls inside a stated range such as "EFD011 through EFD014".

#### Scenario: A new rule missing a documentation location
- **WHEN** an analyzer is added but one of the required locations does not mention its rule ID
- **THEN** the consistency check fails and names the rule and the missing location

#### Scenario: A rule covered by a range
- **WHEN** the README status line says "EFD011 through EFD014"
- **THEN** EFD011, EFD012, EFD013, and EFD014 all count as covered

#### Scenario: A shipped analyzer not registered in the CLI
- **WHEN** the analyzer assembly exports a diagnostic ID that no analyzer in the CLI analyzer set supports
- **THEN** the consistency check fails and names the rule
