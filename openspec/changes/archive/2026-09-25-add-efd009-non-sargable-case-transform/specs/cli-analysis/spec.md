# Spec Delta

## ADDED Requirements

### Requirement: Run EFD009 during workspace analysis
The CLI SHALL include EFD009 in its analyzer set. Case transformations applied to a column in EF Core predicates found in supplied C# projects and solutions SHALL go through the existing reporting, ordering, suppression, privacy, test-project, and exit-code contracts.

#### Scenario: Analyze a project containing a case-transformed column predicate
- **WHEN** a user analyzes a supported production project that contains an unsuppressed EFD009 match
- **THEN** the CLI emits a medium-confidence EFD009 warning in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze untransformed, excluded, or suppressed predicates
- **WHEN** every candidate predicate compares an untransformed column, transforms only the comparison value, falls outside the conservative boundary, or is protected by standard Roslyn suppression
- **THEN** those predicates add no EFD009 finding to the CLI report

#### Scenario: Analyze an EFD009 match in an included test project
- **WHEN** `--include-test-projects` is supplied and EFD009 reports in a project classified as a test project
- **THEN** the CLI retains the finding but downgrades it to advisory confidence and `Info` severity through the shared project-level policy
