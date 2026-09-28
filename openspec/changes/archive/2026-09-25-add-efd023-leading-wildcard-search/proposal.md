# Proposal

## Why

`customer.Name.Contains(term)` and `customer.Email.EndsWith(domain)` are the most common search idioms in EF Core code. EF translates them to a pattern with a leading wildcard, such as `LIKE '%term%'`, or to a function such as `CHARINDEX` over the column. Neither can seek an ordinary index, so every search scans the table. `EF.Functions.Like(column, "%" + term)` has the same problem when the pattern starts with a wildcard.

This is often acceptable on small tables, and is sometimes the only practical option. That is why the product brief ranks EFD023 Medium value and Medium false-positive risk, and says it should ship as advisory.

EFD009 and EFD022 deliberately excluded `Contains` and `EndsWith` so that this rule could own them. That boundary is already in their specs and rule pages.

## What Changes

- Add EFD023. It detects the following on a mapped string column in a SQL-backed predicate of a proven EF Core query:
  - the single-`string`-argument `Contains` or `EndsWith`
  - `EF.Functions.Like` whose pattern provably starts with `%` or `_`

  In every case, the searched value or pattern must not reference the entity.
- Also report when the column is wrapped in `ToLower()` or `ToUpper()`, such as `customer.Name.ToLower().Contains(term)`. EFD009 excluded that shape so this rule reports it once.
- Recognize a leading wildcard in a `Like` pattern when the pattern is:
  - a constant
  - a concatenation whose leftmost operand is such a constant
  - an interpolated string whose first text segment starts with `%` or `_`

  Non-constant patterns stay silent.
- Use the same predicate positions and proven-origin rules as EFD009: `Where` and the predicate overloads of `Queryable` and EF Core async terminals, with no nested lambdas and no predicates after an element-type-changing projection.
- Not reported:
  - `StartsWith`, a prefix search that can seek an index
  - `StringComparison` overloads (EFD022) and the `char` overloads
  - a collection's `Contains(column)` (an `IN` list)
  - column-to-column searches
  - computed properties
  - unproven or in-memory sources
- Emit one finding per search call, at advisory confidence and `Info` severity in the `Performance` category. Evidence names the query origin, the column path, the search form, and the leading-wildcard reason. The remediation depends on the provider:
  - a prefix search when the semantics allow it
  - full-text search: `EF.Functions.Contains`/`FreeText` on SQL Server, `tsvector` or a `pg_trgm` trigram index on PostgreSQL
  - a reversed or computed column for suffix searches
  - accepting the scan for small or rarely searched tables
- Register EFD023 in CLI workspace analysis, with analyzer, fixture, suppression, console, JSON, documentation, release-metadata, README, package, and CHANGELOG coverage. The report schema does not change.

## Capabilities

### New Capabilities

- `efd023-leading-wildcard-search`: Defines advisory detection, exclusions, evidence, provider-aware remediation, suppression, and validation coverage for substring, suffix, and leading-wildcard searches on a column in an EF Core predicate.

### Modified Capabilities

- `cli-analysis`: EFD023 must run in CLI analysis and follow the existing reporting, ordering, suppression, privacy, test-project, and exit-code contracts.

## Impact

- **Code:** one new Roslyn analyzer (`LeadingWildcardSearchAnalyzer`). It needs the same predicate-position, origin, and column-path logic as EFD009. The design decides whether to extract that logic into a shared helper.
- **Tests and fixtures:** analyzer tests, an `EFD023.Sample` CLI fixture, and a case in the shared test-project fixture.
- **Docs and registration:**
  - `docs/rules/EFD023.md`
  - CLI registration
  - an `AnalyzerReleases.Unshipped.md` entry
  - README, PACKAGE.md, and CHANGELOG updates in every place listed in `CLAUDE.md`
- **Unchanged:** EFD023 stays local-only, adds no dependency or code fix, and does not change the JSON schema.
- **Exit code:** an unsuppressed advisory finding still produces exit code `1`, like every finding.
