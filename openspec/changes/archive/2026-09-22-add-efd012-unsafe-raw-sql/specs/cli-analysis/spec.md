# Spec Delta

## ADDED Requirements

### Requirement: Run EFD012 during workspace analysis
The CLI SHALL include EFD012 in its analyzer set so unsafe dynamic SQL passed to supported EF Core raw-SQL APIs in supplied C# projects and solutions flows through the existing reporting, ordering, suppression, privacy, test-project downgrade, and exit-code contracts.

#### Scenario: Analyze a project containing unsafe raw SQL
- **WHEN** a user analyzes a supported production project containing an unsuppressed EFD012 match
- **THEN** the CLI emits a high-confidence EFD012 warning in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze safe or suppressed raw SQL
- **WHEN** candidate raw SQL is safely parameterized or every unsafe match is protected by standard Roslyn suppression
- **THEN** those operations add no EFD012 finding to the CLI report

#### Scenario: Analyze unsafe raw SQL in a test project
- **WHEN** EFD012 reports in a project classified as a test project
- **THEN** the CLI retains the finding but downgrades it to advisory confidence and informational severity through the shared project-level policy
