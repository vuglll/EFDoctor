# Spec Delta

## ADDED Requirements

### Requirement: Run EFD011 during workspace analysis
The CLI SHALL include EFD011 in its analyzer set so direct synchronous blocking on supported EF Core asynchronous operations found in supplied C# projects and solutions flows through the existing reporting, ordering, suppression, privacy, and exit-code contracts.

#### Scenario: Analyze a project containing direct EF async blocking
- **WHEN** a user analyzes a supported project containing an unsuppressed EFD011 match
- **THEN** the CLI emits the EFD011 finding in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze properly awaited or suppressed operations
- **WHEN** candidate EF Core asynchronous operations are properly awaited or every blocking match is protected by standard Roslyn suppression
- **THEN** those operations add no EFD011 finding to the CLI report
