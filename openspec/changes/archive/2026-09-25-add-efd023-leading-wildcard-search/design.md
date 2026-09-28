# Design

## Context

EFD009 (`NonSargableCaseTransformAnalyzer`) already contains everything EFD023 needs to find a column inside a SQL-backed predicate:

- **Predicate operators:** it recognizes `Queryable` and EF async operators that take a predicate, and recovers their single-parameter lambda.
- **Proven origin:** it proves the source with `EfQueryOperationAnalysis.TryAnalyzeSource`.
- **Entity type:** it requires the lambda parameter to be the origin's `DbSet<T>` entity type.
- **Predicate walk:** it walks the predicate body without entering nested lambdas.
- **Column paths:** it recognizes mapped string property paths and builds display paths such as `Order.Customer.Email`.

`EfProviderDetection` identifies a referenced SQL Server or Npgsql provider. EFD022 owns `StringComparison` overloads, and EFD009 owns `ToLower()`/`ToUpper()` compared with `==`, `Equals`, or `StartsWith`. Both explicitly left `Contains` and `EndsWith` to this rule. See `proposal.md` for motivation and the EFD023 and CLI deltas for observable behavior.

## Goals / Non-Goals

**Goals:**

- Report substring, suffix, and provably leading-wildcard searches on mapped columns in SQL-backed predicates, at advisory confidence.
- Share the predicate-scanning logic with EFD009 instead of copying it, without changing any EFD009 result.
- Report every search once, with no overlap with EFD009 or EFD022.

**Non-Goals:**

- Reading database indexes, full-text catalogs, or trigram indexes.
- Evaluating non-constant `Like` patterns, or `Like` patterns that escape a leading wildcard.
- `char` overloads, `IndexOf`, `Regex`, and provider-specific functions such as `EF.Functions.ILike`.
- Searches in nested lambdas, `OrderBy` keys, or projections.
- A code fix.

## Decisions

### Extract a shared predicate-scan helper from EFD009

Move EFD009's predicate-operator sets, origin and entity-type proof, nested-lambda-free walk, and column-path recognition into an internal `EfPredicateScan` helper. The helper resolves the needed symbols once per compilation. For a given invocation, it returns:

- the proven origin
- the operator display name
- the predicate lambda
- an enumeration of the lambda body's operations, excluding nested lambdas

It also provides `TryGetColumnPath(operation, parameter, out path)`.

Refactor EFD009 onto the helper without changing behavior. Its 63 analyzer tests and its end-to-end test must pass unchanged, which guards the move.

Copying the roughly 120 lines into a second analyzer was rejected. The two copies would drift, and the "same boundaries as EFD009" promise in the spec would then be false.

### Match search calls by resolved symbol

A walked invocation is a search candidate when it is one of these:

- **Contains or EndsWith:** the target is `string.Contains` or `string.EndsWith` with exactly one `string` parameter. The instance must be a column path, or a column path wrapped in a parameterless `string.ToLower()`/`ToUpper()` call.
- **Like:** the target's original definition is `Microsoft.EntityFrameworkCore.DbFunctionsExtensions.Like` with 3 or 4 parameters. The `matchExpression` argument must be a column path, optionally case-transformed.

The searched value (for `Contains`/`EndsWith`) or the pattern (for `Like`) must contain no reference to the predicate parameter.

A `string` receiver check excludes collection `Contains`, and the single-`string`-parameter check excludes `StringComparison` and `char` overloads. The `ToLower()`/`ToUpper()` receiver is allowed here because EFD009 only reports case transformations compared with `==`, `Equals`, or `StartsWith`. `x.ToLower().Contains(term)` therefore gets exactly one finding, from EFD023.

### Decide leading wildcards syntactically, and only when provable

For `Like`, the leading character is taken from:

- a constant string: its first character
- a string `+` concatenation: the leftmost operand, recursively
- an interpolated string: the first part, which must be literal text

`%` or `_` means a leading wildcard. Anything else, including a variable, a method call, or an interpolation hole in the first position, means unknown, and nothing is reported. Leading `_` counts because the database cannot seek on a pattern that doesn't start with fixed text.

For `Contains` and `EndsWith`, the leading wildcard is inherent in the translation. EF Core emits either a `LIKE` pattern starting with `%` or a function such as `CHARINDEX` over the column. The rule claims only that no ordinary index seek is possible, which holds for both.

### Advisory confidence with Info severity

The descriptor uses `DiagnosticSeverity.Info`, `Performance` category, and `advisory` confidence. The mapper already turns advisory into an `Info` finding. Advisory is right because many substring searches on small tables are fine, and full-text or trigram indexes are invisible to static analysis. Any unsuppressed finding still makes the scan exit with code `1`. The rule page documents `dotnet_diagnostic.EFD023.severity = none` for teams that want the rule off.

### Location, evidence, and provider-aware remediation

The location is the search invocation: `customer.Name.Contains(term)` or `EF.Functions.Like(customer.Name, "%" + term)`.

Evidence names:

- the origin and operator
- the column path
- the search form and any case transformation
- the reason: `Contains matches anywhere in the value`, `EndsWith matches a suffix`, or `the LIKE pattern starts with '%'`
- the detected provider, when there is one

Impact says a leading-wildcard search can't seek an ordinary index and typically scans. It notes this is often acceptable for small or rarely searched tables.

Remediation has a provider-specific lead and a common tail:

- **SQL Server lead:** full-text search with `EF.Functions.Contains`/`FreeText`.
- **Npgsql lead:** a `pg_trgm` GIN trigram index, which lets `LIKE`/`ILIKE` with leading wildcards use an index, or `tsvector` full-text search.
- **No provider detected:** both options.
- **Common tail:** use `StartsWith` when a prefix search meets the need, use a reversed or computed column for suffix searches, and suppress with a reason when a scan is acceptable or a supporting index exists.

### Keep CLI integration centralized

Register the analyzer once in `WorkspaceAnalyzer` and add an `AnalyzerReleases.Unshipped.md` entry. Follow the documentation checklist in `CLAUDE.md`.

## Risks / Trade-offs

- **Moving shared code could change EFD009 behavior.** → Make it a pure move, run the EFD009 unit and end-to-end suites before and after, and change no EFD009 assertions.
- **The rule is noisy on codebases with many small-table searches.** → Advisory confidence and `Info` severity, plus easy suppression and editorconfig opt-out.
- **A trigram or full-text index may already make the search efficient.** → Name it in the remediation as a reason to suppress. Static analysis can't see it.
- **A `Like` pattern could start with an escaped wildcard.** → A leading escape character is not `%` or `_`, so the rule stays silent. Patterns built through helpers are unknown and also silent.
- **A future EFD009 change could start reporting `ToLower().Contains`.** → The EFD009 spec already excludes `Contains`, and the EFD023 tests include `ToLower().Contains` so any double reporting would show up in the end-to-end fixture.

## Migration Plan

Ship EFD023 enabled by default, register it, and add release metadata and every documentation entry in `CLAUDE.md`. To roll back, remove the registration, analyzer, tests, fixture, and docs. The `EfPredicateScan` extraction can stay, because it doesn't change behavior. No persisted data, report field, dependency, or JSON schema changes.
