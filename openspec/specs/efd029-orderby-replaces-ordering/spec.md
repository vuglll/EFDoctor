# EFD029 OrderBy Replaces Ordering Specification

## Purpose
Defines high-confidence detection and reporting of EF Core queries where a second `OrderBy` or `OrderByDescending` silently discards an earlier ordering in the same inline query chain, when `ThenBy` was almost certainly intended.

## Requirements

### Requirement: Detect an ordering replaced by a later OrderBy
EFD029 SHALL report a semantically resolved `System.Linq.Queryable.OrderBy` or `OrderByDescending` invocation, the *replacing ordering*, when its source is already ordered in the same inline chain. The source is already ordered when walking back from the replacing ordering reaches an earlier resolved `Queryable` `OrderBy`, `OrderByDescending`, `ThenBy`, or `ThenByDescending` through pass-through operators only. The pass-through operators are:

- `Queryable.Where`;
- the EF Core operators `Include`, `ThenInclude`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `AsTracking`, `IgnoreAutoIncludes`, `IgnoreQueryFilters`, `TagWith`, and `TagWithCallSite`;
- the relational operators `AsSplitQuery` and `AsSingleQuery`.

The query SHALL be proven to originate from an EF Core `DbSet<TEntity>` or `DbContext.Set<TEntity>()`. Methods SHALL be identified by resolved symbols, not by method-name text.

#### Scenario: OrderBy followed by OrderBy
- **WHEN** a proven `DbSet` query applies `OrderBy(x => x.A).OrderBy(x => x.B)`
- **THEN** EFD029 reports the second `OrderBy`

#### Scenario: Descending variants
- **WHEN** a proven query applies `OrderByDescending(x => x.A)` followed by `OrderBy(x => x.B)`, or `OrderBy(x => x.A)` followed by `OrderByDescending(x => x.B)`
- **THEN** EFD029 reports the later ordering operator

#### Scenario: Existing ThenBy is also discarded
- **WHEN** a proven query applies `OrderBy(x => x.A).ThenBy(x => x.B).OrderBy(x => x.C)`
- **THEN** EFD029 reports the final `OrderBy`, and the evidence names both discarded operators

#### Scenario: Pass-through operators between the orderings
- **WHEN** pass-through operators such as `Where`, `Include`, `AsNoTracking`, `AsSplitQuery`, or `TagWith` occur between the earlier ordering and the replacing ordering
- **THEN** EFD029 still reports the replacing ordering

#### Scenario: Three consecutive OrderBy calls
- **WHEN** a proven query applies `OrderBy(a).OrderBy(b).OrderBy(c)`
- **THEN** EFD029 reports the second and the third `OrderBy`, one finding each

#### Scenario: Static invocation syntax
- **WHEN** the replacing ordering is written as `Queryable.OrderBy(source, selector)` over an ordered proven query
- **THEN** EFD029 reports it

### Requirement: Leave legitimate re-ordering and unproven shapes alone
EFD029 SHALL NOT report when any operator between the earlier ordering and the replacing ordering is not a pass-through operator. This covers `Skip`, `Take`, `Distinct`, `Select`, `SelectMany`, `GroupBy`, joins, set operations, `Cast`, `OfType`, and unrecognized calls, which either give the earlier ordering a purpose or change the query's shape. EFD029 SHALL NOT report when the earlier ordering reaches the replacing ordering through a local variable, parameter, field, property, or method call. It SHALL NOT report on `Enumerable` ordering operators, on sources not proven to come from EF Core, or on same-named methods that don't resolve to `System.Linq.Queryable`.

#### Scenario: ThenBy after OrderBy
- **WHEN** a proven query applies `OrderBy(x => x.A).ThenBy(x => x.B)`
- **THEN** EFD029 does not report

#### Scenario: Paging between the orderings
- **WHEN** a proven query applies `OrderBy(x => x.A).Take(10).OrderBy(x => x.B)`, or the same shape with `Skip`
- **THEN** EFD029 does not report, because the earlier ordering selects which rows are kept

#### Scenario: Shape-changing operator between the orderings
- **WHEN** `Distinct`, `Select`, `GroupBy`, or another operator outside the pass-through list occurs between the two orderings
- **THEN** EFD029 does not report

#### Scenario: Earlier ordering stored in a local
- **WHEN** an ordered query is assigned to a local, and a later statement applies `OrderBy` to that local
- **THEN** EFD029 does not report

#### Scenario: In-memory ordering
- **WHEN** the orderings apply to an in-memory sequence, including one produced by `AsEnumerable()` or `ToList()` on an EF query
- **THEN** EFD029 does not report

#### Scenario: Unproven or unrelated source
- **WHEN** the orderings apply to an `IQueryable<T>` whose origin is not proven to be an EF Core `DbSet`, or a same-named `OrderBy` resolves outside `System.Linq.Queryable`
- **THEN** EFD029 does not report

#### Scenario: Single ordering
- **WHEN** a proven query applies one `OrderBy` with no earlier ordering in the chain
- **THEN** EFD029 does not report

#### Scenario: Generated or malformed code
- **WHEN** the orderings appear in generated code, or the replacing ordering does not resolve successfully
- **THEN** EFD029 does not report and does not throw

### Requirement: Produce actionable and qualified EFD029 findings
Each EFD029 finding SHALL have:

- rule title `OrderBy discards an earlier ordering`;
- category `Correctness`, `Warning` severity, and high confidence;
- documentation key `EFD029`;
- every field of the shared finding contract populated.

The finding SHALL span from the replacing ordering's method name to the end of its invocation, using one-based coordinates. With static invocation syntax, it SHALL span the whole invocation. Evidence SHALL name the replacing operator, the discarded ordering operators in source order, and the proven EF origin. Impact SHALL explain that EF Core translates only the last ordering, so rows are sorted by the replacing key alone, and ties come back in an order the database doesn't guarantee. It SHALL NOT claim a specific runtime cost. Remediation SHALL recommend `ThenBy` or `ThenByDescending` when both keys were intended, and deleting the earlier ordering when replacing it was intended. The confidence-driven severity mapping and the universal test-project downgrade apply as for other rules.

#### Scenario: Complete structured finding
- **WHEN** EFD029 reports a replacing ordering
- **THEN** console and JSON output contain every shared finding field with documentation key `EFD029`, high confidence, and `Warning` severity

#### Scenario: Exact diagnostic location
- **WHEN** EFD029 reports `set.OrderBy(a).OrderBy(b)`
- **THEN** the range starts at the second `OrderBy` name and ends at the end of its argument list

#### Scenario: Qualified remediation
- **WHEN** EFD029 reports a finding
- **THEN** remediation names `ThenBy`/`ThenByDescending` for a combined sort, and deleting the earlier ordering for an intended replacement

### Requirement: Support standard suppression and existing reporting behavior
EFD029 SHALL use standard Roslyn diagnostic suppression: pragma, editor configuration, and `SuppressMessage("Correctness", "EFD029")`. Findings SHALL flow through the existing local-only CLI, with its deterministic ordering, de-duplication, and stable exit codes. No new command, schema version, network activity, or telemetry is added.

#### Scenario: Pragma suppression
- **WHEN** a replacing ordering is covered by `#pragma warning disable EFD029`
- **THEN** the CLI result excludes that finding until the warning is restored

#### Scenario: Editor configuration suppression
- **WHEN** applicable editor configuration sets `dotnet_diagnostic.EFD029.severity = none`
- **THEN** the CLI result excludes EFD029 for that scope

#### Scenario: SuppressMessage attribute
- **WHEN** the containing member has `[SuppressMessage("Correctness", "EFD029")]`
- **THEN** the finding is excluded, and unsuppressed findings elsewhere still report

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan produces EFD029 findings
- **THEN** both output formats contain the complete finding in deterministic order, keep the successful-scan-with-findings exit code, and use JSON schema version `1`

### Requirement: Validate EFD029 with focused and end-to-end tests
The change SHALL include at least ten positive and ten negative analyzer fixtures that cover every scenario in this specification. It SHALL include at least one CLI end-to-end test that verifies console and JSON output for an EFD029 fixture project.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** the positive and negative EFD029 fixtures verify detection, pass-through and blocking operators, source exclusions, the exact span, and suppression, with no analyzer exceptions

#### Scenario: CLI end-to-end fixture
- **WHEN** the CLI analyzes the EFD029 fixture project in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD029 findings, with exact locations and JSON schema version `1`
