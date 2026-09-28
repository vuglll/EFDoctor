# Spec Delta

## ADDED Requirements

### Requirement: Run EFD013 during workspace analysis
The CLI SHALL include EFD013 in its analyzer set so eligible EF Core load/loop/save update and delete patterns found in supplied C# projects and solutions flow through the existing reporting, ordering, suppression, privacy, test-project downgrade, and exit-code contracts.

#### Scenario: Analyze a project containing an eligible bulk-update candidate
- **WHEN** a user analyzes a supported production project containing an unsuppressed EFD013 update match
- **THEN** the CLI emits a medium-confidence EFD013 warning in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze a project containing an eligible bulk-delete candidate
- **WHEN** a user analyzes a supported production project containing an unsuppressed EFD013 delete match
- **THEN** the CLI emits a medium-confidence EFD013 warning that distinguishes the delete candidate and exits with code `1`

#### Scenario: Analyze excluded or suppressed patterns
- **WHEN** candidate loops contain excluded behavior, lack proven EF Core bulk-operation support, or every eligible match is protected by standard Roslyn suppression
- **THEN** those patterns add no EFD013 finding to the CLI report

#### Scenario: Analyze an EFD013 match in a test project
- **WHEN** EFD013 reports in a project classified as a test project
- **THEN** the CLI retains the finding but downgrades it to advisory confidence and informational severity through the shared project-level policy
