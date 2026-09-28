# Spec Delta

## ADDED Requirements

### Requirement: Run every shipped analyzer during workspace analysis
The CLI SHALL run every EFDoctor analyzer that the analyzer assembly ships against the C# compilations of the supplied target. Findings from every rule SHALL go through the same reporting, ordering, suppression, privacy, test-project, and exit-code contracts, and SHALL keep the severity and confidence their rule assigns except where those shared contracts change them. Each rule's detection behavior is specified in that rule's own capability spec, not in this capability.

#### Scenario: Analyze a project with an unsuppressed finding from any rule
- **WHEN** a user analyzes a supported production project that contains an unsuppressed match for any shipped rule
- **THEN** the CLI reports that finding with its rule-assigned severity and confidence in the selected console or JSON format and exits with code `1`

#### Scenario: Analyze a project with only suppressed or non-matching code
- **WHEN** every candidate pattern in the target falls outside its rule's boundary or is protected by standard Roslyn suppression
- **THEN** those patterns add no finding to the CLI report

#### Scenario: A newly shipped rule
- **WHEN** a new analyzer is added to the analyzer assembly
- **THEN** it runs during workspace analysis without any rule-specific change to this capability

## REMOVED Requirements

### Requirement: Run EFD006 during workspace analysis
**Reason**: Superseded by "Run every shipped analyzer during workspace analysis". Per-rule CLI requirements were applied to only some rules and duplicated the shared reporting, test-project, and exit-code requirements.
**Migration**: EFD006's detection behavior remains specified in its own capability spec, and the repository consistency checks enforce that EFD006 stays registered in the CLI.

### Requirement: Run EFD011 during workspace analysis
**Reason**: Superseded by "Run every shipped analyzer during workspace analysis". Per-rule CLI requirements were applied to only some rules and duplicated the shared reporting, test-project, and exit-code requirements.
**Migration**: EFD011's detection behavior remains specified in its own capability spec, and the repository consistency checks enforce that EFD011 stays registered in the CLI.

### Requirement: Run EFD012 during workspace analysis
**Reason**: Superseded by "Run every shipped analyzer during workspace analysis". Per-rule CLI requirements were applied to only some rules and duplicated the shared reporting, test-project, and exit-code requirements.
**Migration**: EFD012's detection behavior remains specified in its own capability spec, and the repository consistency checks enforce that EFD012 stays registered in the CLI.

### Requirement: Run EFD013 during workspace analysis
**Reason**: Superseded by "Run every shipped analyzer during workspace analysis". Per-rule CLI requirements were applied to only some rules and duplicated the shared reporting, test-project, and exit-code requirements.
**Migration**: EFD013's detection behavior remains specified in its own capability spec, and the repository consistency checks enforce that EFD013 stays registered in the CLI.

### Requirement: Run EFD017 during workspace analysis
**Reason**: Superseded by "Run every shipped analyzer during workspace analysis". Per-rule CLI requirements were applied to only some rules and duplicated the shared reporting, test-project, and exit-code requirements.
**Migration**: EFD017's detection behavior remains specified in its own capability spec, and the repository consistency checks enforce that EFD017 stays registered in the CLI.

### Requirement: Run EFD018 during workspace analysis
**Reason**: Superseded by "Run every shipped analyzer during workspace analysis". Per-rule CLI requirements were applied to only some rules and duplicated the shared reporting, test-project, and exit-code requirements.
**Migration**: EFD018's detection behavior remains specified in its own capability spec, and the repository consistency checks enforce that EFD018 stays registered in the CLI.

### Requirement: Run EFD019 during workspace analysis
**Reason**: Superseded by "Run every shipped analyzer during workspace analysis". Per-rule CLI requirements were applied to only some rules and duplicated the shared reporting, test-project, and exit-code requirements.
**Migration**: EFD019's detection behavior remains specified in its own capability spec, and the repository consistency checks enforce that EFD019 stays registered in the CLI.

### Requirement: Run EFD025 during workspace analysis
**Reason**: Superseded by "Run every shipped analyzer during workspace analysis". Per-rule CLI requirements were applied to only some rules and duplicated the shared reporting, test-project, and exit-code requirements.
**Migration**: EFD025's detection behavior remains specified in its own capability spec, and the repository consistency checks enforce that EFD025 stays registered in the CLI.

### Requirement: Run EFD009 during workspace analysis
**Reason**: Superseded by "Run every shipped analyzer during workspace analysis". Per-rule CLI requirements were applied to only some rules and duplicated the shared reporting, test-project, and exit-code requirements.
**Migration**: EFD009's detection behavior remains specified in its own capability spec, and the repository consistency checks enforce that EFD009 stays registered in the CLI.

### Requirement: Run EFD023 during workspace analysis
**Reason**: Superseded by "Run every shipped analyzer during workspace analysis". Per-rule CLI requirements were applied to only some rules and duplicated the shared reporting, test-project, and exit-code requirements.
**Migration**: EFD023's detection behavior remains specified in its own capability spec, and the repository consistency checks enforce that EFD023 stays registered in the CLI.
