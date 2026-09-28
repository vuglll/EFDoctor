# Proposal

## Why

In round-1 corpus validation (`validation/FINDINGS.md`, defect 4), EFD001 scored 3% strict and 23% detection precision. That is far below the 90% gate for a high-confidence rule. 24 of its 31 findings (3 in Jellyfin, 21 in Smartstore) save **once per batch**, which is the batching EFD001's own remediation recommends:

- `foreach (var chunk in ids.Chunk(100)) { …; await db.SaveChangesAsync(); }`
- `while ((await pager.ReadNextPageAsync<T>()).Out(out var page)) { foreach (var item in page) {…} await db.SaveChangesAsync(); }`
- `while (true) { var page = query.ToPagedList(++index, 500); foreach (var item in page) {…} await db.SaveChangesAsync(); if (!page.HasNextPage) break; }`
- `await foreach (var item in items) { …; processed++; if (processed >= Limit) { await db.SaveChangesAsync(); db.ChangeTracker.Clear(); processed = 0; } }`

EFD001 treats the loop around the save as a per-item loop, so it reports all of them.

## What Changes

EFD001 no longer reports a save whose innermost enclosing loop saves once per batch. A loop does that when any of the following holds:

1. **Batch iteration variable:** a `foreach`/`await foreach` whose element type is itself a collection (not `string`), for example the arrays that `Chunk(n)` produces, or a sequence of pages.
2. **Page declared by the loop condition:** a `while`, `do`, or `for` condition that declares or assigns a collection-typed local, for example `.Out(out var page)`, `is { } page`, or `(page = …)`.
3. **Page loaded in the loop body:** a `while`, `do`, or `for` body that declares a collection-typed local, and iterates it with a `foreach` that finishes before the save.
4. **Threshold flush:** the save sits inside an `if` whose condition compares a batch threshold. That means a local counter incremented within the loop, compared with `>=`, `>`, or `==`, directly or through `%`; or the `Count`/`Length` of a collection compared the same way.

A save inside a per-item loop is still reported. That includes a per-item loop nested inside a batch loop, and a save guarded by an ordinary condition such as `if (item.IsTransient)`.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd001-detection`:
  - Adds a requirement to skip saves that run once per batch.
  - Narrows the nested-`if` scenario of "Recognize supported loop execution scopes" to exclude threshold flushes.

## Impact

- **Code:** `SaveChangesInLoopAnalyzer`. It uses the semantic model it already has, and no shared helpers change.
- **Tests:** new analyzer fixtures. All EFD001 scenarios get spec traits (this is the first time the capability is traced).
- **Docs:** the analyzer description, `docs/rules/EFD001.md`, the README EFD001 boundary, and the CHANGELOG.
- **Validation:** re-run Jellyfin and Smartstore. The 24 batch false positives should go away, and the true positive and the per-item `acceptable` findings should stay.
