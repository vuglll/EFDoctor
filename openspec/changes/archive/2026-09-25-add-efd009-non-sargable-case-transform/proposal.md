# Proposal

## Why

`entity.Email.ToLower() == input` is a common way to write a case-insensitive lookup in EF Core. EF translates it to `LOWER([Email]) = @input`. Wrapping the column in a function prevents an index seek on that column, so the lookup scans a table that grows over time. On SQL Server's default case-insensitive collation, the transformation doesn't even change the result. The product brief lists EFD009 as a Medium-High value rule, and it can reuse the proven-origin and property-path machinery that EFD004, EFD005, and EFD019 already use.

## What Changes

- Add EFD009. It detects the parameterless `string.ToLower()` or `string.ToUpper()` applied to a mapped string column inside a SQL-backed predicate lambda of a proven EF Core query, when the transformed column is compared with a value that does not reference the entity.
- Recognized comparisons: `==`, `!=`, `string.Equals(a, b)`, `a.Equals(b)`, and `StartsWith(value)`. Unlike `Contains` and `EndsWith`, these can use an index when the column is not transformed.
- Recognized predicate positions: `Where` and the predicate overloads of `Queryable` and EF Core async terminals (`Any`, `All`, `Count`, `LongCount`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, and their `…Async` forms).
- Not reported:
  - a transformed comparison value, such as `entity.Email == input.ToLower()`
  - column-to-column comparisons
  - `Contains` and `EndsWith`, which are already non-sargable and belong to EFD023
  - `ToLowerInvariant`, `ToUpperInvariant`, and culture overloads, whose translation depends on the EF Core version and provider
  - nested lambdas, computed properties, predicates after an element-type-changing projection, and unproven or in-memory sources
- Emit one medium-confidence performance warning per transformed column occurrence. Evidence names the column path, transformation, comparison, and query origin. The remediation is provider-aware: SQL Server gets collation advice, PostgreSQL gets `citext`, `ILike`, or expression-index advice, and it names the expression-index exception.
- Register EFD009 in CLI workspace analysis, with analyzer, fixture, suppression, console, JSON, documentation, release-metadata, README, package, and CHANGELOG coverage. The report schema does not change.

## Capabilities

### New Capabilities

- `efd009-non-sargable-case-transform`: Defines conservative semantic detection, exclusions, evidence, provider-aware remediation, suppression, and validation coverage for case transformations applied to a column in an EF Core predicate.

### Modified Capabilities

- `cli-analysis`: EFD009 must run in CLI analysis and follow the existing reporting, ordering, suppression, privacy, test-project, and exit-code contracts.

## Impact

- **Code:** one new Roslyn analyzer (`NonSargableCaseTransformAnalyzer`). It reuses `EfQueryOperationAnalysis` for origin proof and mapped-property checks, so shared helpers don't change.
- **Tests and fixtures:** analyzer tests, an `EFD009.Sample` CLI fixture, and a case in the shared test-project fixture.
- **Docs and registration:** `docs/rules/EFD009.md`, CLI registration, an `AnalyzerReleases.Unshipped.md` entry, and README, PACKAGE.md, and CHANGELOG updates.
- **Unchanged:** EFD009 stays local-only, reads provider identity only from compile-time references, adds no dependency or code fix, and does not change the JSON schema.
- **Confidence:** medium, because a function-based index on `LOWER(column)` can make the predicate sargable and EFDoctor cannot see database indexes.
