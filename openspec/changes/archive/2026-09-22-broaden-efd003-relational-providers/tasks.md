# Tasks

## 1. Confirm provider markers

- [x] 1.1 Confirm the exact metadata name of the Npgsql `UseNpgsql` extension type against the referenced Npgsql.EntityFrameworkCore.PostgreSQL package (expected `Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsExtensions`); record the confirmed name in the helper. Verify by resolving it with `GetTypeByMetadataName` in a scratch test that references the package.
- [x] 1.2 Confirm the metadata name of the Pomelo MySQL `UseMySql` extension type used for the auto-index exclusion; record it. Verify the same way against the referenced Pomelo package.

## 2. Shared provider-detection helper

- [x] 2.1 Add an internal `EfProviderDetection` static class in `src/EFDoctor.Analyzers/` that resolves referenced relational providers from a `Compilation` via marker types and classifies each by whether its database auto-creates foreign-key indexes; expose a method that returns the eligible provider (name + identity) only for allow-list providers (SQL Server, Npgsql) and reports when a known auto-indexing provider (MySQL) is present.
- [x] 2.2 Define multi-provider precedence: a known auto-indexing provider suppresses eligibility even when an allow-list provider is also referenced. Verify with provider-detection unit tests covering: SQL Server only, Npgsql only, MySQL only, SQL Server + MySQL, none, and an unrelated relational reference.

## 3. Switch EFD003 to the helper

- [x] 3.1 Replace the inline `SqlServerDbContextOptionsExtensions` marker lookup in `MissingForeignKeyIndexAnalyzer.StartCompilation` with a call to `EfProviderDetection`, keeping the `ModelSnapshot` presence check; store the detected provider for evidence. Verify existing EFD003 SQL Server tests still pass unchanged.
- [x] 3.2 Include the detected provider name in the finding evidence string. Verify with a test asserting the evidence contains the provider for both SQL Server and Npgsql.

## 4. Test harness and fixtures

- [x] 4.1 Extend `AnalyzerTestHarness` with an `includeNpgsql` (and `includeMySql`) option that adds the provider assembly and its dependencies, mirroring the existing `includeSqlServer` path. Verify a harness smoke test compiles a source referencing each provider without errors.
- [x] 4.2 Add Npgsql model-snapshot positive fixtures mirroring the existing SQL Server EFD003 cases (single-property uncovered FK, composite uncovered FK, covered-by-index, covered-by-PK/alternate-key, base-entity coverage) and assert identical outcomes to the SQL Server equivalents.
- [x] 4.3 Add a negative fixture proving EFD003 does not report when the only provider is a known auto-indexer (MySQL), and a multi-provider fixture asserting the precedence from task 2.2.
- [x] 4.4 Add a negative fixture for an unknown/absent supported provider (relational reference without an allow-list provider) asserting no finding.

## 5. Documentation and multi-provider repositioning

- [x] 5.1 Update `docs/rules/EFD003.md` to describe the supported-provider allow-list (SQL Server, PostgreSQL), the deliberate exclusion of auto-indexing providers (MySQL) and unknown providers, and the provider named in evidence. Verify every documented example agrees with a fixture.
- [x] 5.2 Update README EFD003 guidance: revise the "EFD003 model-snapshot boundary" section and the EFD003 rule-table/summary wording for the supported-provider set. Verify no remaining EFD003 text claims it is SQL-Server-only.
- [x] 5.3 Reposition the README from "EF Core / SQL Server" to a multi-provider EF Core tool: update the tagline (line 3) and intro (line 5), and revise the `Non-SQL Server providers` non-goal (line 123) — most rules are provider-agnostic and EFD003 supports SQL Server + PostgreSQL, so the remaining SQL-Server-specific scope is only the optional future live-connection feature. Verify the README no longer frames the whole tool as SQL-Server-only and no non-goal contradicts a shipped rule.
- [x] 5.4 Reposition `docs/product-brief.md` to a multi-provider EF Core tool: update the title/summary (lines 2, 8, 12), the market/differentiation framing (e.g., line 166 "EF Core + SQL Server specifically"), and the EFD003 row. State that SQL Server is the most-validated provider and PostgreSQL is supported, without claiming equal coverage everywhere. Verify the brief no longer positions the product as SQL-Server-only.
- [x] 5.5 Sweep the repository for any remaining "EF Core and SQL Server"/"SQL-Server-only" product-scope claims (README, docs, CLI help/banner text) and reconcile them with the multi-provider positioning. Verify with a grep that surviving SQL-Server-specific mentions are intentional (the optional live-connection non-goal, provider-validation notes) rather than stale scope claims.

## 6. Verification and finalization

- [x] 6.1 Build the solution from a clean state with the documented .NET command and verify no errors or warnings.
- [x] 6.2 Run the full automated suite and verify all EFD001–EFD006, EFD011–EFD013, provider-detection, and CLI end-to-end tests pass, with the new Npgsql/MySQL EFD003 cases included.
- [x] 6.3 Confirm no other rule's behavior changed by rerunning their fixtures and confirming no new or lost findings.
- [x] 6.4 Sync the `efd003-missing-foreign-key-index` delta to the main spec (including the Purpose edit removing "SQL Server"-specific wording) and run strict OpenSpec validation for the change.
- [x] 6.5 Manually run the CLI against a PostgreSQL fixture project in console and JSON modes and verify EFD003 reports with correct evidence, severity, exact coordinates, stable exit codes, and no network or telemetry activity.
