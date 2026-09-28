# Spec Delta

## ADDED Requirements

### Requirement: Run EFD006 during workspace analysis
The CLI SHALL include EFD006 in its analyzer set so eligible sibling collection include chains found in supplied C# projects and solutions flow through the existing reporting, ordering, suppression, privacy, and exit-code contracts.

#### Scenario: Analyze a project containing sibling collection includes
- **WHEN** a user analyzes a supported project containing an unsuppressed EFD006 match
- **THEN** the CLI emits the EFD006 finding in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze a split-query or suppressed match
- **WHEN** every otherwise matching EFD006 query is protected by `AsSplitQuery` or standard Roslyn suppression
- **THEN** those queries add no EFD006 finding to the CLI report
