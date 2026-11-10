# Proposal

## Why

Many EF Core applications project queries to DTOs with AutoMapper's `ProjectTo<T>(…)` instead of `Select`. The chain proof doesn't recognize it, so a query that passes through `ProjectTo` ends the proof:

```csharp
return await context.Series
    .Where(s => s.LibraryId == libraryId)
    .ProjectTo<SeriesDto>(mapper.ConfigurationProvider)
    .ToListAsync();
```

Validation round 2 found that Kavita ends 116 of its 254 `ToListAsync` chains in `ProjectTo`. EFD005 and every other rule built on the chain proof are silent on those queries. EFD006 also misses an `AsSplitQuery()` written after `ProjectTo`, in Kavita's `LibraryRepository.GetLibraryDtoByIdAsync`. It analyzes the chain that ends at the last `Include` before `ProjectTo`, and doesn't see the split mode. This is the first item in the validation log's next steps.

## What Changes

- The shared EF Core query-chain proof treats AutoMapper's `ProjectTo<TDestination>(…)` as a projection, like `Queryable.Select`. It continues through the call to its source query and records `AutoMapper.ProjectTo` in the proven operations. `ProjectTo` doesn't bound the row count.
  - Both forms are recognized: the `AutoMapper.QueryableExtensions.Extensions.ProjectTo` extension method, and the `ProjectTo` instance method of an AutoMapper mapper (`IMapper`), whose first argument is the source query.
  - Recognition is by symbol: the method's name, its containing type's namespace (`AutoMapper` or `AutoMapper.QueryableExtensions`), one type argument, and a first parameter of type `IQueryable` or `IQueryable<T>`. EFDoctor takes no dependency on AutoMapper.
- The origin proof for conditionally composed locals is unchanged. `ProjectTo` changes the element type, so a write through it still isn't a self-composition.
- EFD006's include-chain walk continues through `ProjectTo`, so `AsSplitQuery` or `AsSingleQuery` after it counts as an explicit split mode, and the chain is reported once, from its outermost end.
- Rules that skip a projection because it doesn't return tracked entities also skip `ProjectTo`:
  - EFD002 (`FirstOrDefault` checked only for null);
  - EFD026 (`Update` on an entity the context already tracks);
  - EFD032 (change to an `AsNoTracking` entity that is never saved);
  - EFD038 (bulk operation that leaves tracked entities stale).
- **Not changed:** EFD017 still reports only an `Include` dropped by `Queryable.Select`. The includes before `ProjectTo` are also ignored by EF Core, but whether a mapping profile reads a navigation can't be seen in the query. EFD037 is unchanged: a `ProjectTo` result isn't an entity, so EFD037 can't report it.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `query-chain-local-tracking`: adds a requirement that the chain proof follows AutoMapper `ProjectTo` as a projection, and that rules which skip a projection also skip it.
- `efd006-multiple-collection-include`: the split-query boundary requirement says that the include chain continues through `ProjectTo`, so a split mode after it is honored.

EFD002, EFD026, EFD032, and EFD038 gain the projection exclusion through the shared requirement. EFD005 and the other chain-proof rules gain recall through the shared proof. Their own specs don't name the proof's boundaries, so they don't change.

## Impact

- **Code:**
  - `EfQueryOperationAnalysis` gains a `ProjectTo` recognizer and passes through it in `TryAnalyzeSourceCore`.
  - `MultipleCollectionIncludeAnalyzer` passes through it.
  - `CountUsedForExistenceAnalyzer`, `UpdateOnTrackedEntityAnalyzer`, `UntrackedChangeLostAnalyzer`, and `StaleTrackedEntitiesAnalyzer` treat `AutoMapper.ProjectTo` as a projection.
- **Tests:** an AutoMapper stub in test sources; positive fixtures for EFD005 and EFD006's split mode after `ProjectTo`; negative fixtures for the four projection-skipping rules and for a `ProjectTo` from an unrelated namespace.
- **Docs:** the CHANGELOG's Unreleased section, and `validation/FINDINGS.md`.
- **Validation:** re-run the corpus, and triage every finding that is new compared with `dev`. Any false-positive class blocks shipping.
