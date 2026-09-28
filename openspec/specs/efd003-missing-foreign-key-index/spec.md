# EFD003 Missing Foreign-Key Index Specification

## Purpose

Define high-confidence, local detection of foreign keys in supported EF Core relational model snapshots that lack a usable covering index or key.

## Requirements

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

### Requirement: EFD003 identifies uncovered foreign keys semantically
For each foreign key represented in an eligible current snapshot, the analyzer SHALL report EFD003 when the dependent property sequence is not covered by an index or key applicable to that entity. It SHALL use resolved EF Core API symbols and constant model metadata rather than matching invocation text. It SHALL produce one finding for each uncovered foreign key.

#### Scenario: Single-property foreign key has no index
- **WHEN** a snapshot defines a foreign key on dependent property `CustomerId` and no applicable index or key covers `CustomerId`
- **THEN** EFD003 reports one finding for that foreign key

#### Scenario: Composite foreign key has no index
- **WHEN** a snapshot defines a foreign key on dependent properties `TenantId, CustomerId` and no applicable index or key covers that ordered sequence
- **THEN** EFD003 reports one finding for that composite foreign key

#### Scenario: Multiple uncovered foreign keys
- **WHEN** a snapshot contains multiple foreign keys without covering indexes or keys
- **THEN** EFD003 reports one finding for each uncovered foreign key

#### Scenario: Unrelated method name
- **WHEN** code invokes a method named `HasForeignKey`, `HasIndex`, or `HasKey` that does not resolve to the relevant EF Core model-building API
- **THEN** that invocation does not contribute to an EFD003 finding or suppress one

#### Scenario: Unresolved model metadata
- **WHEN** the analyzer cannot semantically resolve the EF Core builder operation, entity identity, or constant ordered property sequence for a foreign key
- **THEN** EFD003 does not report that foreign key

### Requirement: Covering indexes and keys prevent EFD003
An index or key SHALL cover a foreign key when it applies to the dependent entity or an applicable base entity and its ordered property sequence begins with the complete ordered foreign-key property sequence. Matching SHALL use the property identities represented in the snapshot and SHALL be ordinal. Additional trailing index or key properties SHALL remain covering; missing leading properties, reversed properties, and coverage on an unrelated entity SHALL not be covering.

#### Scenario: Exact index coverage
- **WHEN** the dependent entity has an index whose properties exactly equal the foreign-key property sequence
- **THEN** EFD003 does not report that foreign key

#### Scenario: Wider index has the foreign key as its leading prefix
- **WHEN** a foreign key uses `TenantId, CustomerId` and an applicable index uses `TenantId, CustomerId, CreatedAt`
- **THEN** EFD003 does not report that foreign key

#### Scenario: Composite index has the wrong order
- **WHEN** a foreign key uses `TenantId, CustomerId` and the only candidate index uses `CustomerId, TenantId`
- **THEN** EFD003 reports the foreign key

#### Scenario: Foreign key is not the leading prefix
- **WHEN** a foreign key uses `CustomerId` and the only candidate index uses `TenantId, CustomerId`
- **THEN** EFD003 reports the foreign key

#### Scenario: Primary or alternate key coverage
- **WHEN** an applicable primary or alternate key begins with the complete foreign-key property sequence
- **THEN** EFD003 does not report that foreign key

#### Scenario: Index belongs to another entity
- **WHEN** a matching property sequence is indexed only on an unrelated entity
- **THEN** EFD003 reports the foreign key on the dependent entity

#### Scenario: Base-entity coverage
- **WHEN** the snapshot proves that the dependent entity inherits an applicable covering index or key from a base entity
- **THEN** EFD003 does not report that foreign key

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

### Requirement: Inconclusive or non-source evidence does not trigger EFD003
The analyzer SHALL report only when the eligible snapshot source proves both the foreign key and the absence of model coverage. It SHALL NOT treat comments, strings, inactive preprocessor code, migration history alone, raw SQL, live database metadata, or assumptions about externally managed indexes as model evidence.

#### Scenario: Comments and strings
- **WHEN** comments or string literals contain text resembling foreign-key or index configuration
- **THEN** EFD003 does not use that text as evidence

#### Scenario: Inactive preprocessor branch
- **WHEN** relevant-looking configuration exists only in inactive preprocessor code
- **THEN** EFD003 does not use that configuration as evidence

#### Scenario: Historical migration omits an index
- **WHEN** an old migration contains a foreign key without a corresponding index but the current model snapshot is covered
- **THEN** EFD003 does not report from the historical migration

#### Scenario: Dynamic property metadata
- **WHEN** a foreign-key or candidate index property sequence depends on non-constant runtime computation that cannot be reconstructed reliably
- **THEN** EFD003 does not report that foreign key

### Requirement: EFD003 supports standard suppression and existing reporting behavior
EFD003 SHALL participate in the existing deterministic console and JSON reporting pipeline and stable CLI exit-code behavior. Developers SHALL be able to suppress an intentional finding using standard Roslyn diagnostic suppression mechanisms, including pragma, editor configuration, and `SuppressMessage` where supported, without a proprietary suppression format.

#### Scenario: Pragma suppression
- **WHEN** EFD003 is disabled for an eligible source region with a standard Roslyn pragma
- **THEN** no EFD003 finding from that region appears in console or JSON output

#### Scenario: Editor configuration suppression
- **WHEN** EFD003 severity is set to `none` through applicable editor configuration
- **THEN** the suppressed EFD003 finding does not appear in console or JSON output

#### Scenario: Suppression with justification is documented
- **WHEN** a developer intentionally accepts the indexing tradeoff
- **THEN** EFD003 documentation shows a standard suppression example that records a human-readable justification

#### Scenario: Deterministic multiple-finding output
- **WHEN** a scan produces EFD003 together with other findings
- **THEN** all findings retain the existing deterministic ordering and output contracts

### Requirement: EFD003 is verified by focused and end-to-end tests
The change SHALL include at least ten positive and ten negative EFD003 analyzer fixtures. Coverage SHALL include single and composite foreign keys, exact and prefix coverage, wrong-order and wrong-entity indexes, primary or alternate key coverage, inherited coverage, unresolved metadata, unrelated same-named methods, multiple findings, exact locations, and standard suppression. At least one end-to-end test SHALL invoke the CLI against a fixture project and verify EFD003 console and JSON results.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** at least ten positive and ten negative EFD003 cases verify the specified detection and exclusion behavior

#### Scenario: CLI end-to-end output
- **WHEN** the CLI analyzes the EFD003 fixture project in console and JSON modes
- **THEN** both outputs contain the expected actionable EFD003 finding data and JSON retains the existing schema version

#### Scenario: Clean verification
- **WHEN** the repository is built and tested from a clean checkout with its documented commands
- **THEN** the solution builds successfully and all EFD001, EFD002, EFD003, and CLI tests pass without requiring network communication at analysis time
