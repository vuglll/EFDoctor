# Spec Delta

## ADDED Requirements

### Requirement: Run EFD023 during workspace analysis
The CLI SHALL include EFD023 in its analyzer set. Substring, suffix, and leading-wildcard searches on a column in EF Core predicates found in supplied C# projects and solutions SHALL go through the existing reporting, ordering, suppression, privacy, test-project, and exit-code contracts.

#### Scenario: Analyze a project containing a leading-wildcard search
- **WHEN** a user analyzes a supported production project that contains an unsuppressed EFD023 match
- **THEN** the CLI emits an advisory-confidence EFD023 finding with `Info` severity in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze prefix, excluded, or suppressed searches
- **WHEN** every candidate search is a prefix search, falls outside the conservative boundary, or is protected by standard Roslyn suppression
- **THEN** those searches add no EFD023 finding to the CLI report

#### Scenario: Analyze an EFD023 match in an included test project
- **WHEN** `--include-test-projects` is supplied and EFD023 reports in a project classified as a test project
- **THEN** the CLI retains the finding at advisory confidence and `Info` severity through the shared project-level policy
