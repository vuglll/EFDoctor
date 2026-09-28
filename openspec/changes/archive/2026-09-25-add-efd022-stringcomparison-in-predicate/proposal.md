# Proposal

## Why

EF Core cannot translate the `StringComparison` overloads of `string` methods (`Equals`, `StartsWith`, `EndsWith`, `Contains`, `IndexOf`, and `string.Compare`) into SQL. When one appears inside a query predicate, EF Core 3.0+ throws an `InvalidOperationException` at runtime (client evaluation of predicates is disabled by default), so a query that compiles and looks correct fails only when it executes. It is a high-value correctness bug with very low false-positive risk because the shape — a `string` method with a `System.StringComparison` argument inside a proven database-query lambda — is unambiguous and statically decidable.

## What Changes

- Add rule **EFD022**: report a `System.String` method call that takes a `System.StringComparison` argument when it occurs inside a lambda passed to a resolved `System.Linq.Queryable` operator whose source traces to an EF Core `DbSet`/`DbContext.Set<TEntity>()`.
- Report at high confidence, anchored on the offending string-method call.
- Register EFD022 in the CLI analyzer set so it flows through the existing reporting, ordering, suppression, privacy, test-project, and exit-code contracts.

## Capabilities

### New Capabilities

- `efd022-stringcomparison-in-predicate`: Detect a non-translatable `StringComparison` string comparison inside an EF Core query predicate.

### Modified Capabilities

None.

## Impact

- New analyzer `src/EFDoctor.Analyzers/StringComparisonInPredicateAnalyzer.cs`, reusing existing public helpers on `EfQueryOperationAnalysis` (`TryAnalyzeSource`, `NormalizeMethod`) with no edits to shared analysis code, so it does not collide with parallel query-rule work.
- `src/EFDoctor.Cli/WorkspaceAnalyzer.cs`: add the analyzer to the analyzer set; `AnalyzerReleases.Unshipped.md`: register EFD022.
- New rule doc `docs/rules/EFD022.md`; README rule table and CHANGELOG updated.
- New analyzer fixtures and tests (`tests/Fixtures/EFD022.Sample`, `StringComparisonInPredicateAnalyzerTests`) plus a CLI end-to-end test.
- No JSON schema, CLI command surface, exit-code catalogue, network, or telemetry change; other rules are unaffected. `StringComparison` calls nested inside an in-memory `IEnumerable` operator within a predicate (for example over a navigation via `Enumerable.Any`) are a deliberate out-of-scope limitation for this version.
