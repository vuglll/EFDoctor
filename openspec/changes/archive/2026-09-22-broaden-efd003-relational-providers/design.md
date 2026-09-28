# Design

## Context

See `proposal.md` for motivation. EFD003 is implemented in `MissingForeignKeyIndexAnalyzer`. Its `StartCompilation` gate resolves two marker types and returns early unless both exist:

- `Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot` (the relational migrations snapshot base — present for any relational provider)
- `Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions` (SQL-Server-only)

Everything after the gate — the `SnapshotCollector` reading `HasForeignKey`/`HasIndex`/`HasKey`/`HasBaseType`/`Entity` builder calls, the leading-prefix covering-index correlation, entity/base-entity resolution, and the finding shape — uses only provider-agnostic EF Core relational APIs. The analyzer test harness (`AnalyzerTestHarness`) already parameterizes references with `includeRelational` and `includeSqlServer` flags and pulls provider assemblies from the referenced package graph.

## Goals / Non-Goals

**Goals:**

- Run EFD003 on PostgreSQL (Npgsql) projects with the same correctness bar it has on SQL Server.
- Encode provider eligibility as an explicit, testable allow-list keyed on the "does this database auto-index foreign keys?" property.
- Extract provider detection into one shared, reusable helper so future rules gate consistently.

**Non-Goals:**

- Changing the covering-index correlation logic, finding shape, JSON schema, CLI surface, or suppression mechanism.
- Reading provider information from the live database, connection strings, or runtime configuration — detection is compile-time reference-based only.
- Provider-specific *new* rules (JSONB, arrays, `citext`, etc.). This change only enables that direction; it does not add such rules.
- Fluent-API model reading (EFD003 already reads only the generated `ModelSnapshot`, which is provider-agnostic and captures Fluent configuration).

## Decisions

### 1. Eligibility is an allow-list of providers that do NOT auto-index foreign keys (W1)

The original SQL-Server-only gate was a guard against cross-provider false positives, and that risk is concrete: **MySQL/InnoDB automatically creates an index on a foreign-key column when the constraint is declared**, so a model whose snapshot lacks an explicit FK index is *not* a real gap on MySQL. SQL Server and PostgreSQL do **not** auto-create FK indexes, so a missing model index is a genuine opportunity on both.

Therefore eligibility is not "any relational provider". EFD003 runs only when the compilation references a provider on an explicit allow-list of engines known not to auto-index foreign keys:

| Provider | Marker type (metadata name) | Auto-indexes FK? | EFD003 |
|---|---|---|---|
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions` | No | eligible |
| PostgreSQL (Npgsql) | `Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsExtensions` | No | eligible |
| MySQL (Pomelo) | Pomelo `UseMySql` extension marker | Yes | not eligible |
| Any other / unknown | — | unknown | not eligible (conservative) |

Unknown providers stay ineligible so the "no cross-provider false positive" property is preserved by default; adding a provider is a deliberate, tested act. The exact Npgsql/MySQL marker metadata names are confirmed against the referenced package versions during implementation (task 1).

Alternative considered: gate on `ModelSnapshot` alone (i.e., "any relational provider"). Rejected because it would fire on MySQL and produce false positives — the exact failure the original guard avoided.

Alternative considered: detect the FK-auto-index property from provider metadata/annotations at runtime. Rejected: not available statically and far heavier than a reference-based allow-list.

### 2. Shared provider-detection helper (W2)

Add an internal static helper (working name `EfProviderDetection`) in `EFDoctor.Analyzers` that takes a `Compilation` and resolves which known relational providers are referenced, via `GetTypeByMetadataName` on the marker types above. It exposes the detected provider identity (or set) and a predicate expressing the property EFD003 needs, e.g. `TryGetForeignKeyIndexProvider(Compilation, out ProviderInfo)` returning true only for allow-list providers, with the provider's display name for evidence.

`MissingForeignKeyIndexAnalyzer.StartCompilation` replaces its inline SQL-Server marker lookup with a single call to this helper, keeping the `ModelSnapshot` presence check. The helper is the one place provider markers and their auto-index classification live, so a future provider-specific rule reuses the same detection and classification without duplicating marker strings.

Alternative considered: keep detection inline in the analyzer. Rejected because the second consumer (future rules) would copy marker strings and classification, which is exactly the drift item 2 exists to prevent.

### 3. Provider identity in evidence (W1)

Because EFD003 now applies on more than one provider, the finding evidence names the detected provider so a reader understands why the rule fired and can reason about provider-specific exceptions. This is an additive evidence-string change; the finding contract and JSON schema are unchanged.

## Risks / Trade-offs

- **[Wrong or version-drifted marker metadata name for Npgsql/MySQL]** → Confirm the exact type names against the referenced package versions in task 1 and cover them with provider-detection unit tests; a wrong name simply fails to detect (conservative: EFD003 stays off) rather than misfiring.
- **[A project references multiple providers (e.g., SQL Server + MySQL)]** → Define precedence explicitly: if any referenced provider is a known auto-indexer, suppress to avoid a false positive on that engine; otherwise run when any allow-list provider is present. Covered by a multi-provider fixture.
- **[Npgsql snapshot annotations differ from SQL Server]** → The collector reads only provider-agnostic builder calls (`HasForeignKey`/`HasIndex`/`HasKey`), not annotations; validate with a real Npgsql-generated snapshot fixture.
- **[New test dependencies (Npgsql, Pomelo MySQL) enlarge the test package graph]** → Reference them only in the analyzer test project; no product dependency changes.

## Migration Plan

1. Confirm Npgsql and Pomelo MySQL marker metadata names against referenced package versions.
2. Add `EfProviderDetection` with unit tests.
3. Switch EFD003's gate to the helper; add provider identity to evidence.
4. Extend the test harness with Npgsql (and MySQL, for the negative case) references; add Npgsql snapshot positive fixtures, a MySQL non-report fixture, and a multi-provider precedence fixture.
5. Update `docs/rules/EFD003.md` and README; sync spec and archive.

No persisted data or schema migration. Rollback restores the SQL-Server-only marker gate; schema-version-1 consumers are unaffected because the finding contract does not change.
