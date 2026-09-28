# Spec Delta

## RENAMED Requirements

- FROM: `### Requirement: EFD003 analyzes the current SQL Server model snapshot`
- TO: `### Requirement: EFD003 analyzes the current model snapshot of a supported provider`

## MODIFIED Requirements

### Requirement: EFD003 analyzes the current model snapshot of a supported provider
The analyzer SHALL inspect source that semantically resolves to an EF Core `ModelSnapshot` implementation for a project that references a supported relational provider whose database does not automatically create indexes for foreign-key columns. SQL Server and PostgreSQL (Npgsql) SHALL be supported providers. It SHALL analyze the snapshot's `BuildModel` representation of the current model without constructing a `DbContext`, executing application code, applying migrations, or connecting to a database. EFD003 SHALL NOT report when the only resolvable relational provider is known to auto-create foreign-key indexes, and SHALL NOT report when no supported provider can be resolved. Provider classification SHALL be conservative: a provider that is not on the supported allow-list SHALL be treated as ineligible rather than assumed to need an explicit index.

#### Scenario: Generated snapshot is eligible
- **WHEN** a project references a supported relational provider (SQL Server or PostgreSQL) and a class derives from EF Core `ModelSnapshot` with a semantically valid `BuildModel` override
- **THEN** EFD003 evaluates the foreign keys, indexes, keys, and entity relationships represented by that snapshot

#### Scenario: Generated PostgreSQL snapshot is eligible
- **WHEN** a project references the EF Core PostgreSQL (Npgsql) provider and a class derives from EF Core `ModelSnapshot` with a semantically valid `BuildModel` override
- **THEN** EFD003 evaluates the foreign keys, indexes, keys, and entity relationships represented by that snapshot

#### Scenario: Ordinary model-building code is not a snapshot
- **WHEN** equivalent fluent configuration appears only in a `DbContext.OnModelCreating` method or an arbitrary helper
- **THEN** EFD003 does not report from that code

#### Scenario: Snapshot is unavailable
- **WHEN** a project contains no resolvable EF Core `ModelSnapshot`
- **THEN** EFD003 produces no finding and does not execute the project to reconstruct the model

#### Scenario: Provider auto-creates foreign-key indexes
- **WHEN** the target project's only resolvable relational provider is known to auto-create indexes for foreign-key columns
- **THEN** EFD003 produces no finding because the database already covers the foreign key

#### Scenario: Non-SQL Server project
- **WHEN** the target project does not resolve any supported relational provider, including when it resolves only a relational provider that is not on the supported allow-list
- **THEN** EFD003 produces no finding

### Requirement: EFD003 findings are actionable and accurately located
Each EFD003 diagnostic SHALL have rule title `Foreign key is missing a covering index`, severity `Warning`, and confidence `High`. Its location SHALL span the complete foreign-key builder invocation that establishes the dependent property sequence. The diagnostic SHALL populate the shared finding contract with the snapshot source path, exact start and end positions, entity identity, ordered foreign-key properties, the detected supported provider, impact, remediation, and documentation key `EFD003`.

#### Scenario: Finding evidence
- **WHEN** EFD003 reports an uncovered foreign key
- **THEN** the evidence identifies the model snapshot, dependent entity, ordered foreign-key properties, the detected supported provider, and absence of a covering index or key in the analyzed snapshot

#### Scenario: Performance impact is qualified
- **WHEN** EFD003 reports a finding
- **THEN** its impact explains that missing foreign-key indexes can cause additional scans or work for joins and referential updates or deletes without claiming that a measured slowdown or definite defect exists

#### Scenario: Remediation includes legitimate exceptions
- **WHEN** EFD003 reports a finding
- **THEN** its remediation recommends reviewing and adding an EF model index and migration where appropriate while acknowledging small tables, write-heavy workloads, deliberate index tradeoffs, and indexes managed outside EF migrations

#### Scenario: Exact location
- **WHEN** the snapshot contains an uncovered foreign key builder invocation
- **THEN** console and JSON output use the exact one-based start and end line and column of that invocation
