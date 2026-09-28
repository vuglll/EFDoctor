# Spec Delta

## ADDED Requirements

### Requirement: Run EFD019 during workspace analysis
The CLI SHALL include EFD019 in its analyzer set so EF Core queries materialized and then immediately reduced in supplied C# projects and solutions flow through the existing reporting, ordering, suppression, privacy, test-project downgrade, and exit-code contracts.

#### Scenario: Analyze a project containing a materialize-then-reduce query
- **WHEN** a user analyzes a supported production project containing an unsuppressed EFD019 match
- **THEN** the CLI emits a high-confidence EFD019 warning in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze query-side reductions or suppressed matches
- **WHEN** every reduction runs on the query itself, crosses an explicit client boundary, reuses a stored materialized value, or is protected by standard Roslyn suppression
- **THEN** those expressions add no EFD019 finding to the CLI report

#### Scenario: Analyze an EFD019 match in a test project
- **WHEN** EFD019 reports in a project classified as a test project
- **THEN** the CLI retains the finding but downgrades it to advisory confidence and informational severity through the shared project-level policy
