# Spec Delta

## ADDED Requirements

### Requirement: Run EFD017 during workspace analysis
The CLI SHALL include EFD017 in its analyzer set so eligible EF Core include-before-projection patterns found in supplied C# projects and solutions flow through the existing reporting, ordering, suppression, privacy, test-project downgrade, and exit-code contracts.

#### Scenario: Analyze a project containing an ineffective include
- **WHEN** a user analyzes a supported production project containing an unsuppressed EFD017 match
- **THEN** the CLI emits a high-confidence EFD017 warning in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze preserved or suppressed patterns
- **WHEN** every candidate projection preserves an entity-bearing result, falls outside the conservative flow boundary, or is protected by standard Roslyn suppression
- **THEN** those patterns add no EFD017 finding to the CLI report

#### Scenario: Analyze an EFD017 match in a test project
- **WHEN** EFD017 reports in a project classified as a test project
- **THEN** the CLI retains the finding but downgrades it to advisory confidence and informational severity through the shared project-level policy
