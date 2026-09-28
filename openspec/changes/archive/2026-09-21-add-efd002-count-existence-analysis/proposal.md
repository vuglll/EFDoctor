# Proposal

## Why

EFDoctor currently validates its architecture with EFD001 but does not yet detect wasteful existence checks that count every matching row. EFD002 adds the next high-confidence EF Core rule and exercises reuse of the existing analyzer, finding, suppression, CLI, and reporting pipeline without broadening the product beyond local static analysis.

## What Changes

- Add EFD002 to identify semantically resolved EF Core query patterns where `Count` or `CountAsync` is used only to decide whether any matching row exists.
- Recognize direct zero/one existence comparisons, including equivalent operand ordering and synchronous or awaited asynchronous forms, while avoiding comparisons that require the exact count.
- Restrict synchronous matches to `System.Linq.Queryable.Count` calls whose source can be traced to an EF Core `DbSet`; recognize EF Core `CountAsync` by its declaring extension type. Do not report LINQ-to-Objects or unresolved/same-name methods.
- Emit one high-confidence, suppressible diagnostic per matching count invocation with actionable evidence, database round-trip impact, and `Any`/`AnyAsync` remediation.
- Reuse deterministic console and schema-versioned JSON reporting, and add EFD002 rule documentation, analyzer fixtures, and CLI end-to-end coverage.
- Document the first-version precision boundary: no interprocedural or general data-flow inference for counts stored and compared later, and no synchronous report when an arbitrary `IQueryable` cannot be shown statically to originate from EF Core.

## Capabilities

### New Capabilities

- `efd002-count-existence`: Defines semantic matching, supported existence-test expressions, non-triggering cases, finding guidance, suppression, and reporting expectations for EFD002.

### Modified Capabilities

None. No main capability specs have been archived yet; EFD002 reuses the current finding and CLI contracts without changing their requirements.

## Impact

- Adds a Roslyn analyzer rule and metadata under `src/EFDoctor.Analyzers` and registers it with the CLI's existing analyzer execution path.
- Extends analyzer tests with at least ten positive and ten negative EFD002 cases, including exact source locations and standard suppression.
- Extends CLI/reporting and process-level fixture tests to verify console and JSON findings, deterministic mixed-rule ordering, and unchanged exit-code behavior.
- Adds `docs/rules/EFD002.md` and updates the rule documentation index or README as needed.
- Introduces no network calls, telemetry, code fixes, database connectivity, publication workflow, or new CLI command surface.
