# Spec Delta

## ADDED Requirements

### Requirement: Run EFD025 during workspace analysis
The CLI SHALL include EFD025 in its analyzer set. Redundant or duplicate EF Core include paths found in supplied C# projects and solutions SHALL go through the existing reporting, ordering, suppression, privacy, test-project, and exit-code contracts.

#### Scenario: Analyze a project containing a redundant include
- **WHEN** a user analyzes a supported production project that contains an unsuppressed EFD025 match
- **THEN** the CLI emits a high-confidence EFD025 finding with `Info` severity in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze distinct, branching, or suppressed include patterns
- **WHEN** every include path in the analyzed queries is distinct and not covered by another path, is a repeated prefix used only to branch into a different `ThenInclude`, falls outside the conservative flow boundary, or is protected by standard Roslyn suppression
- **THEN** those patterns add no EFD025 finding to the CLI report

#### Scenario: Analyze an EFD025 match in an included test project
- **WHEN** `--include-test-projects` is supplied and EFD025 reports in a project classified as a test project
- **THEN** the CLI retains the finding but downgrades it to advisory confidence and `Info` severity through the shared project-level policy
