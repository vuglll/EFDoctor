# Proposal

## Why

An EF Core `IQueryable` is a deferred query, not a result. Storing one in a local and then enumerating it more than once — `q.Count()` then `q.ToList()`, or `if (q.Any()) foreach (var x in q)` — silently re-executes the SQL against the database each time. The code reads as if `q` holds data, so the extra round trips are invisible in review and grow with call frequency. It is a real performance bug that is statically detectable: a local of `IQueryable` type, proven to originate from a `DbSet`, that is executed two or more times in the same method.

## What Changes

- Add rule **EFD020**: report a local variable whose type is `System.Linq.IQueryable<T>` and whose initializer traces to an EF Core `DbSet`, when that local is executed (materialized, aggregated, or `foreach`-enumerated) two or more times in the same method body.
- Anchor the finding on the query's local declaration and state how many executions were observed.
- Report at medium confidence (a deliberate re-execution is occasionally intended). Skip a local that is reassigned, to stay conservative.
- Register EFD020 in the CLI analyzer set so it flows through the existing reporting, ordering, suppression, privacy, test-project, and exit-code contracts.

## Capabilities

### New Capabilities

- `efd020-multiple-enumeration`: Detect an EF Core `IQueryable` local that is enumerated or re-executed more than once.

### Modified Capabilities

None.

## Impact

- New analyzer `src/EFDoctor.Analyzers/MultipleEnumerationAnalyzer.cs`, using a `RegisterOperationBlockAction` over method bodies and reusing the existing public `EfQueryOperationAnalysis` helpers (`TryAnalyzeSource`, `NormalizeMethod`, `Unwrap`) with no edits to shared analysis code.
- `src/EFDoctor.Cli/WorkspaceAnalyzer.cs`: add the analyzer to the analyzer set; `AnalyzerReleases.Unshipped.md`: register EFD020.
- New rule doc `docs/rules/EFD020.md`; README rule table and CHANGELOG updated.
- New analyzer fixtures and tests (`tests/Fixtures/EFD020.Sample`, `MultipleEnumerationAnalyzerTests`) plus a CLI end-to-end test.
- No JSON schema, CLI command surface, exit-code catalogue, network, or telemetry change; other rules are unaffected. Cross-method flow (an `IQueryable` returned or passed to another method and enumerated there) is a deliberate out-of-scope limitation for this version.
