# Proposal

## Why

EFD003 (missing foreign-key index) is the only EFDoctor rule that never runs on a non-SQL-Server project: it is hard-gated on the `Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions` marker and returns nothing otherwise. Its own analysis — reading `HasForeignKey`/`HasIndex`/`HasKey`/`HasBaseType` from an EF Core `ModelSnapshot` — is entirely provider-agnostic, and the "a foreign key without a covering index needs extra work for joins and referential updates" guidance is equally true on PostgreSQL, which (like SQL Server) does not auto-create indexes on foreign-key columns. PostgreSQL/Npgsql users therefore silently lose a high-confidence rule for no technical reason. The original SQL-Server-only scope was a deliberate first-version guard against cross-provider false positives; broadening it correctly requires an explicit, provider-aware allow-list rather than "any relational provider".

## What Changes

- Replace EFD003's single SQL-Server marker gate with a **provider allow-list** of relational providers that do **not** auto-create foreign-key indexes: SQL Server and PostgreSQL (Npgsql). EFD003 runs when the compilation references a provider on this allow-list.
- Keep EFD003 conservative for providers where a missing model index is not a real gap or is unknown. In particular, when a referenced provider is known to auto-create foreign-key indexes (for example MySQL/InnoDB via Pomelo), EFD003 does **not** report for that model, preserving the "no cross-provider false positive" principle. Unknown providers remain skipped.
- Introduce a **shared provider-detection helper** in `EFDoctor.Analyzers` that identifies which relational EF Core providers a compilation references and classifies them by the "does the database auto-index foreign keys?" property. EFD003 consumes it, and it is available to future provider-specific rules so provider gating is defined in one place rather than duplicated per rule.
- Update EFD003 rule documentation and evidence wording to describe the supported-provider set instead of "SQL Server" specifically. Provider identity appears in finding evidence so a reader knows why the rule applied.
- Reposition the product-level documentation (README and product brief) from "EF Core / SQL Server" to a **multi-provider EF Core** tool. This reflects existing reality — eight of the nine rules (EFD001, EFD002, EFD004, EFD005, EFD006, EFD011, EFD012, EFD013) already operate on any relational EF Core provider — plus EFD003's new SQL Server + PostgreSQL support. SQL Server remains the most-validated provider; the framing changes, not a claim of equal coverage everywhere.

## Capabilities

### New Capabilities

None. The provider-detection helper is an internal enabler with no observable behavior of its own; its behavior is exercised and specified through EFD003's eligibility rules.

### Modified Capabilities

- `efd003-missing-foreign-key-index`: Redefines rule eligibility from "the SQL Server provider is referenced" to "a supported provider that does not auto-index foreign keys is referenced (SQL Server or PostgreSQL)"; adds explicit non-reporting when a referenced provider auto-indexes foreign keys or is unknown; adds provider identity to evidence. The covering-index correlation logic is unchanged.

## Impact

- `src/EFDoctor.Analyzers/MissingForeignKeyIndexAnalyzer.cs`: replace the SQL-Server marker gate with the shared provider-detection helper; include provider identity in evidence.
- New internal helper type in `src/EFDoctor.Analyzers/` (for example `EfProviderDetection`) that resolves provider markers from a `Compilation`.
- `docs/rules/EFD003.md` and README EFD003 guidance: describe the supported-provider set and the deliberate exclusion of auto-indexing/unknown providers.
- `README.md`: reposition to multi-provider EF Core — the tagline, intro, the `Non-SQL Server providers` non-goal, and the EFD003 model-snapshot section all need updating to reflect that most rules are provider-agnostic and EFD003 now supports SQL Server + PostgreSQL.
- `docs/product-brief.md`: reposition the "EF Core / SQL Server" framing (title, summary, market/differentiation sections, EFD003 row) to a multi-provider EF Core tool, while stating SQL Server is the most-validated provider.
- `tests/EFDoctor.Analyzers.Tests/`: extend the analyzer test harness to reference the Npgsql provider (and a MySQL provider for the negative case), and add Npgsql model-snapshot fixtures plus provider-detection unit tests. No CLI command surface, JSON schema, network, or telemetry change; other rules are unaffected.
