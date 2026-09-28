# Spec Delta

## ADDED Requirements

### Requirement: Run EFD018 during workspace analysis
The CLI SHALL include EFD018 in its analyzer set so discarded EF Core asynchronous operation tasks found in supplied C# projects and solutions flow through the existing reporting, ordering, suppression, privacy, test-project downgrade, and exit-code contracts.

#### Scenario: Analyze a project containing a discarded EF Core task
- **WHEN** a user analyzes a supported production project containing an unsuppressed EFD018 match
- **THEN** the CLI emits a high-confidence EFD018 warning in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze observed or suppressed tasks
- **WHEN** every EF Core asynchronous operation task is awaited, returned, stored, passed on, or protected by standard Roslyn suppression
- **THEN** those operations add no EFD018 finding to the CLI report

#### Scenario: Analyze an EFD018 match in a test project
- **WHEN** EFD018 reports in a project classified as a test project
- **THEN** the CLI retains the finding but downgrades it to advisory confidence and informational severity through the shared project-level policy
