# Proposal

## Why

`Include(b => b.Posts)` loads every related row. When the code then only ever uses a filtered subset in memory, the query transfers, materializes, and tracks rows that are thrown away:

```csharp
var blog = await db.Blogs.Include(b => b.Posts).FirstAsync(b => b.Id == id);
var published = blog.Posts.Where(p => p.Published).ToList();
```

EF Core 5 added filtered include, `Include(b => b.Posts.Where(p => p.Published))`, which applies the filter in SQL. The roadmap lists this shape as an advisory EFD024.

## What Changes

- Add **EFD024, `Include`d collection filtered in memory**: `Performance`, `Info`, advisory. It reports a plain `Include(x => x.Nav)` of a collection navigation when all of these hold:
  - **The query:** it is the chain of a proven EF Core query, materialized into a local by a single-entity terminal or by `ToList`/`ToArray` (or their awaited async forms).
  - **The entities:** in the method, the loaded entity is the local itself. For a list, it is a `foreach` variable over the list, or the lambda parameter of an `Enumerable` call on the list.
  - **The uses:** every read of `entity.Nav` is the source of an `Enumerable` predicate operator (`Where`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `Any`, `All`, `Count`, or `LongCount`) with a predicate argument, and there is at least one such read.
  - **The local doesn't escape:** the entity local, the list, and the entity variables are otherwise only read through properties, or iterated as above.
- **Not reported:**
  - an already filtered `Include`;
  - a navigation also used unfiltered;
  - entities returned, passed to a method, or stored;
  - reference navigations.

## Capabilities

### New Capabilities

- `efd024-include-filtered-in-memory`: detection, boundaries, finding contract, suppression, and tests for EFD024.

### Modified Capabilities

None.

## Impact

- **Code:** a new `IncludeFilteredInMemoryAnalyzer`, with registration and the release entry.
- **Tests:** analyzer fixtures, `tests/Fixtures/EFD024.Sample` with end-to-end tests, and an `EFD005.TestSample` case.
- **Docs:** `docs/rules/EFD024.md`, and every rule list and table.
- **Validation:** a corpus re-run, with every finding triaged.
