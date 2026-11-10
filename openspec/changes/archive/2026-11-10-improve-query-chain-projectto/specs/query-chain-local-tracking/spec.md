# Spec Delta

## ADDED Requirements

### Requirement: Follow AutoMapper ProjectTo as a projection
The shared EF Core query-chain proof SHALL continue through a call to AutoMapper's `ProjectTo<TDestination>` to its source query, as it does through `Queryable.Select`, and SHALL record `AutoMapper.ProjectTo` in the proven operations. The call SHALL NOT bound the row count. A `ProjectTo` that is not AutoMapper's, recognized by symbol, SHALL end the proof as before.

#### Scenario: Extension ProjectTo
- **WHEN** a method calls `context.Items.Where(i => i.Active).ProjectTo<ItemDto>(configuration).ToList()`, where `ProjectTo` is the extension method of `AutoMapper.QueryableExtensions`
- **THEN** the proof traces the query to the `DbSet` origin `context.Items`, and the proven operations are `Queryable.Where -> AutoMapper.ProjectTo`

#### Scenario: Mapper ProjectTo
- **WHEN** a method calls `mapper.ProjectTo<ItemDto>(context.Items.Where(i => i.Active)).ToList()`, where `mapper` is an AutoMapper `IMapper`
- **THEN** the proof traces the query through the first argument to the `DbSet` origin `context.Items`

#### Scenario: ProjectTo in a stored query
- **WHEN** a query local is initialized with `context.Items.ProjectTo<ItemDto>(configuration)` and a later statement executes it
- **THEN** the proof follows the local and the `ProjectTo` call to the `DbSet` origin

#### Scenario: ProjectTo is not a row bound
- **WHEN** a proven query is projected with `ProjectTo` and materialized with `ToList` or `ToListAsync`, with no other bound
- **THEN** EFD005 reports, as it would for the same query projected with `Select`

#### Scenario: Unrelated ProjectTo
- **WHEN** a query passes through a method named `ProjectTo` whose containing type is not in the `AutoMapper` or `AutoMapper.QueryableExtensions` namespace
- **THEN** the proof does not continue through that call

#### Scenario: ProjectTo is not a self-composition
- **WHEN** a conditionally composed query local is reassigned as `query = query.ProjectTo<ItemDto>(configuration);`
- **THEN** the origin proof does not follow that local, because `ProjectTo` changes the element type

### Requirement: Rules that skip a projection skip ProjectTo
EFD002, EFD026, EFD032, and EFD038 SHALL treat a query whose proven operations contain `AutoMapper.ProjectTo` as a projection: its results are new objects that the context doesn't track, even when the destination type is an entity type.

#### Scenario: ProjectTo result is not a tracked entity
- **WHEN** a query projected with `ProjectTo` is loaded with `FirstOrDefault`, `First`, `ToList`, or their async forms, and its result is only checked for null, passed to `Update`, changed without a save, or followed by a bulk operation on the same entity type
- **THEN** EFD002, EFD026, EFD032, and EFD038 do not report
