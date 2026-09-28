# Design

## Context

Two analyzers already walk inline EF Core include chains:

- `MultipleCollectionIncludeAnalyzer` (EFD006) starts at the outermost supported composition call. It uses `IsSourceOfSupportedComposition` so each inline chain is analyzed once, and it records collection includes by property symbol.
- `ProjectionDropsIncludeAnalyzer` (EFD017) walks back from a `Select`. It proves `DbSet<T>`/`DbContext.Set<T>()` origins, allows a fixed set of element-preserving `Queryable`, EF, and relational methods, and builds full `Include`/`ThenInclude` property paths with `TryGetIncludeSegments`.

`TryGetIncludeSegments` strips filtered-include operators to reach the navigation. That is correct for EFD017 but loses the one fact EFD025 needs: whether a segment was filtered. `EfQueryOperationAnalysis` supplies method normalization, source extraction, lambda recovery, and unwrapping. The CLI consumes the shared diagnostic-property contract unchanged. See `proposal.md` for motivation and the EFD025 and CLI spec deltas for observable behavior.

## Goals / Non-Goals

**Goals:**

- Build the complete path of every include chain in one proven inline query, and compare paths by resolved property symbols.
- Report every redundant chain once, and never report a chain whose removal would change what the query loads.
- Share include-path extraction with EFD017 instead of maintaining a third copy, without changing any EFD006 or EFD017 result.

**Non-Goals:**

- Reading the EF model, including `AutoInclude()` configuration, owned-type auto-loading, or navigation-name mapping for string includes.
- Deciding whether two filtered includes are equivalent, or merging a filtered include with an unfiltered one.
- Following include state through locals, conditional query building, helpers, or across element-type-changing operators.
- A code fix or automatic removal of the redundant call.

## Decisions

### Analyze each inline chain once, from its outermost supported call

Register a compilation-start invocation action after resolving `Queryable`, `DbSet<T>`, `DbContext`, `EntityFrameworkQueryableExtensions`, and optionally `RelationalQueryableExtensions`. Continue only when the invocation is a supported element-preserving call that is not itself the source of another supported call. This is the EFD006 pattern. When the parent is a materializer or `Select`, the include chain beneath it is still analyzed. When the parent is another `Where`, the parent is analyzed instead.

Use the same supported method sets as EFD017: `Where`, ordering, `Skip`, `Take`, and `Distinct`; EF `Include`, `ThenInclude`, tracking modes, `IgnoreAutoIncludes`, `IgnoreQueryFilters`, and tags; relational `AsSplitQuery`/`AsSingleQuery`. Move these sets into `EfQueryOperationAnalysis` so both rules share one definition. An unsupported call, `Enumerable` operator, local reference, or unproven origin rejects the whole chain.

Starting at each `Include` and searching both directions was rejected. It would need cross-invocation deduplication, and it would examine the same chain once for every include.

### Model include chains in source order

Walk from the entry call down to the origin, then record includes while the recursion unwinds, so the list is in source order. Each `Include` starts a new `IncludeChain`. The chain holds:

- its first and last invocation
- its kind: expression, string, or unusable
- its segments: property symbols for an expression chain, ordinal strings for a string chain

Each resolved `ThenInclude` appends its segments to the most recent chain and updates that chain's last invocation. If any segment is filtered, casts or type-tests the parameter, or is not a plain property path rooted at the parameter, the chain becomes unusable. Once unusable, it stays unusable.

A string include is usable only when the argument's `ConstantValue` is a non-empty string whose `.`-separated segments are all non-empty. The string overload returns a plain `IQueryable<T>`, so `ThenInclude` cannot continue it and a string chain is always one call.

### Extract a shared include-segment helper with a filtered flag

Move segment extraction into an internal `EfIncludePaths` helper. It returns the property segments and reports whether filtered-include operators were stripped. EFD017 calls it and ignores the flag, so its behavior stays the same. EFD025 treats a filtered segment as unusable. The existing EFD017 suite is the regression guard for the move. EFD006 keeps its own single-level collection matcher, because its question is cardinality, not path identity.

Copying the logic into EFD025 was rejected. A third copy of the filter-stripping loop would drift from the other two.

### Compare paths by symbol and pick one reason per chain

Compare expression segments with `SymbolEqualityComparer.Default` on `OriginalDefinition`, so property names that match as text but belong to different types never compare equal. Compare string segments ordinally. Never compare a string chain with an expression chain.

For each usable chain `c` in source order:

- **Covered:** `c` is covered if some other usable chain of the same kind has a path that `c`'s path is a strict prefix of. Evidence names the first such covering chain in source order.
- **Duplicate:** otherwise, `c` is a duplicate if an earlier usable chain of the same kind has an equal path. Evidence names the first earlier match.

Each chain produces at most one diagnostic. The earliest occurrence of every maximal path is never reported. So deleting every reported chain leaves the set of loaded navigations unchanged, and the findings never conflict with one another. Chain counts are small, so the O(n²) comparison needs no index.

### Report a tight span on the redundant chain

Reduced syntax: the location starts at the `Name` of the `Include` member access and ends at the end of the chain's last invocation. The span covers `Include(...).ThenInclude(...)` without the receiver. Static extension syntax has no member access to anchor on, so the location is the full syntax of the chain's last invocation. The EFD006 whole-invocation span was rejected because it starts at the query origin, which makes sibling findings hard to tell apart.

Use `DiagnosticSeverity.Info`, category `Maintainability`, and confidence `high`. Evidence names the origin, the redundant path, the covering or earlier path, and the reason (`duplicate` or `covered by a longer path`). Impact says that EF Core merges include paths, so the SQL and loaded data do not change and the cost is misleading query intent. Remediation says to delete the redundant chain, and notes that a repeated leading `Include` that branches into a different `ThenInclude` is required and is never reported.

### Keep CLI integration centralized

Register the analyzer once in `WorkspaceAnalyzer` and add an `AnalyzerReleases.Unshipped.md` entry. The mapper already turns `Info` diagnostics with high confidence into `Info` findings. Ordering, rendering, exit codes, suppression, privacy, and the test-project policy do not change.

## Risks / Trade-offs

- **An `Info`-severity cleanup finding still makes the exit code `1` and can fail CI.** → This is the documented exit-code contract. The rule page shows `dotnet_diagnostic.EFD025.severity = none` for teams that want the rule off.
- **Moving `TryGetIncludeSegments` could change EFD017 results by accident.** → Move it without changing behavior, and run the EFD017 analyzer and end-to-end suites before and after.
- **String and expression includes of the same navigation are missed.** → This is deliberate. Mapping a string to a CLR property needs the model. Revisit it if real-project validation shows the mix is common.
- **Conditional query building (`if (x) q = q.Include(...)`) is the most common source of duplicates, and it is out of scope.** → Document the inline-only boundary, and consider bounded reaching definitions after validation, as EFD017 does.
- **EFD006 and EFD017 may also report on the same query.** → The rules describe different problems. EFD006 already deduplicates by property, so a duplicate include never inflates its count.

## Migration Plan

Ship EFD025 enabled by default and register it in the CLI. Add release metadata, rule documentation, and README and CHANGELOG entries, then run the focused and full suites. To roll back, remove the registration, analyzer, tests, fixture, and docs. The shared helper extraction can stay, because it does not change behavior. No persisted data, report field, dependency, or JSON schema changes.
