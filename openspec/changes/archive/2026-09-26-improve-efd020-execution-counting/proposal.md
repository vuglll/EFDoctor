# Proposal

## Why

In round-1 corpus validation (`validation/FINDINGS.md`, defect 5), EFD020 scored 0% strict and 6% detection precision: 16 of its 17 findings were false positives. EFD020 counts three things as executions that are not repeated executions:

- **Subquery composition (13, Jellyfin).** The local is used as `ids.Contains(x.Id)` or `members.Any(...)` inside another query's `Where` lambda. That lambda is an expression tree, so EF Core translates the use into one SQL statement. The local never executes on its own.
- **Predicate terminals (2, Smartstore).** Calls such as `settings.AnyAsync(x => x.Name == "A")` and `ruleSets.FirstOrDefault(x => …)` each run a *different* query over the same base. They are not a re-execution.
- **Mutually exclusive branches (1, Smartstore).** `async ? await q.ToDictionaryAsync() : q.ToDictionary()` runs exactly one arm.

## What Changes

- A use inside a lambda converted to an expression tree (`Expression<TDelegate>`) is **not** an execution. A use inside an ordinary delegate lambda still counts, because it runs on the client.
- A terminal operator called with a `predicate` argument is **not** an execution of the local. Terminals without a predicate, and terminals with a selector (such as `Sum(x => x.Total)` or `ToDictionary(x => x.Id)`), still count.
- Executions in the two arms of the same conditional (`?:` or `if`/`else`) are not added together. The count is the largest number of executions that can happen on a single path through the method.
- The reporting threshold (two or more), the anchor, the reassignment exclusion, and the finding contract are unchanged.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd020-multiple-enumeration`: "Detect multiple enumeration of an IQueryable local" narrows what counts as an execution and how executions are counted.

## Impact

- **Code:** `MultipleEnumerationAnalyzer` only.
- **Tests:**
  - New EFD020 analyzer fixtures.
  - All EFD020 scenarios get spec traits. This is the first time this capability is traced.
- **Docs:**
  - `docs/rules/EFD020.md`
  - the README EFD020 boundary
  - the analyzer description
  - the CHANGELOG
- **Validation:**
  - Re-run Jellyfin and Smartstore.
  - Expect the 16 false positives to go away.
  - Expect the one `acceptable` finding (`country.FirstOrDefault()` called twice) to remain.
