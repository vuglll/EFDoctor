# Proposal

## Why

EF Core raw-SQL APIs deliberately bypass interpolation safety when callers use `FromSqlRaw` or `ExecuteSqlRaw`, so composing their SQL text from interpolation or concatenation can introduce SQL injection and unstable query-plan shapes. EFD012 is the next rule in the product brief's recommended order and offers a high-value, high-confidence safety check that fits the existing semantic analyzer architecture.

## What Changes

- Add EFD012 detection for semantically resolved EF Core `FromSqlRaw`, `ExecuteSqlRaw`, and `ExecuteSqlRawAsync` calls whose SQL argument is directly interpolated, concatenated, or supplied through a local value proven to originate from those unsafe forms.
- Avoid reports for constant SQL, parameter placeholders with separate arguments, interpolated-safe EF APIs, unrelated same-named methods, and values whose unsafe origin cannot be proven.
- Emit high-confidence, actionable findings with precise evidence, qualified impact language, safe remediation, documentation, suppression coverage, and positive/negative fixtures.
- Register EFD012 in CLI workspace analysis and verify console, JSON, ordering, exit-code, local-only, and test-project downgrade behavior.

## Capabilities

### New Capabilities

- `efd012-unsafe-raw-sql`: Defines semantic detection, exclusions, finding content, suppression, and validation coverage for unsafe SQL construction passed to EF Core raw-SQL APIs.

### Modified Capabilities

- `cli-analysis`: Requires EFD012 to run in the CLI analyzer set and flow through existing reporting and execution contracts.

## Impact

The change adds one Roslyn analyzer, analyzer and CLI fixtures/tests, a rule document, CLI analyzer registration, release metadata, and README status updates. It uses existing Roslyn and EF Core references and introduces no runtime dependency, network access, telemetry, schema-version change, or automatic code fix.
