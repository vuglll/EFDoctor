# Design

## Context

`EfQueryOperationAnalysis.TryAnalyzeSourceCore` follows a call only when its method belongs to `System.Linq.Queryable`, or is a bound-neutral EF Core composition method. `ProjectTo` is neither, so the proof fails at it. EFD006 has its own include-chain walk, `MultipleCollectionIncludeAnalyzer.TryAnalyzeChain`. It accepts `Queryable`, EF Core, and relational extension methods. It starts at the outermost supported call, so a chain broken by `ProjectTo` is analyzed from the last `Include` before it, without any split mode written after it.

AutoMapper exposes `ProjectTo` in two forms:
- `AutoMapper.QueryableExtensions.Extensions.ProjectTo<TDestination>(this IQueryable source, IConfigurationProvider configuration, …)`, with several overloads;
- `IMapper.ProjectTo<TDestination>(IQueryable source, …)`, an instance method whose source is its first argument.

See proposal.md for motivation.

## Goals / Non-Goals

**Goals:**
- Recover every chain-proof rule's view of queries that pass through either form of `ProjectTo`.
- Let EFD006 see a split mode written after `ProjectTo`.
- Keep rules that rely on "results are tracked entities" silent on `ProjectTo`.

**Non-Goals:**
- Reading AutoMapper profiles to learn which members a projection reads. That is what EFD017 would need.
- Other projection libraries, such as Mapster's `ProjectToType` or `Select` helpers. They can follow the same pattern later, if the corpus shows them.
- Rule-specific walkers other than EFD006's, such as those of EFD017, EFD025, and EFD029. They stop at a projection today, which is correct for them.

## Decisions

### Recognize ProjectTo by symbol shape, not by assembly

**Decision:** A call is AutoMapper's `ProjectTo` when all of these hold:
- the method is named `ProjectTo` and has exactly one type argument;
- its containing type's namespace is `AutoMapper` or `AutoMapper.QueryableExtensions`;
- its first parameter (ordinal 0, the `this` parameter of the extension form) is `System.Linq.IQueryable` or `System.Linq.IQueryable<T>`;
- it returns `System.Linq.IQueryable<TDestination>`.

The source is the argument bound to that first parameter, for both forms.

**Why:** AutoMapper has moved `ProjectTo` between classes and changed its overloads across major versions. A namespace and shape check covers all of them, and test sources can declare a stub without a package reference. AutoMapper's recent versions need a commercial license, so the tests shouldn't depend on the package.

**Alternatives considered:**
- Matching the metadata name `AutoMapper.QueryableExtensions.Extensions` only. Rejected: it misses `IMapper.ProjectTo`, and it breaks if the class is renamed.
- Matching any method named `ProjectTo`. Rejected: an unrelated method of that name may run a client query or return a different source.

### One recognizer, shared by the chain proof and EFD006

**Decision:** `EfQueryOperationAnalysis.TryGetProjectToSource(IInvocationOperation, out IOperation source)` holds the check. `TryAnalyzeSourceCore` calls it before the `Queryable`/EF check, recurses into the source with the same hop count, and appends `AutoMapper.ProjectTo`. `MultipleCollectionIncludeAnalyzer` calls it in `TryAnalyzeChain` and in its "is this the outermost call" check (`IsSupportedComposition`/`IsSourceOfSupportedComposition`).

**Why:** One definition keeps the two walks in step. `IsSourceOfSupportedComposition` must accept `ProjectTo` too, or the inner `Include` would still be treated as a chain end, and EFD006 would report twice or ignore the later split mode.

### Projection-sensitive rules check the operation name

**Decision:** `EfQueryOperationAnalysis.ProjectToOperation` is `"AutoMapper.ProjectTo"`.
- EFD002's `IsProjection` and EFD026's projection list include it.
- EFD032's and EFD038's load checks return "not a tracked entity load" when the operations contain it.

**Why:** These rules already read `Operations` for `Select` or tracking modifiers, so this follows their existing pattern. In each rule, `ProjectTo` to a DTO is already excluded by an element-type check. The explicit check also covers `ProjectTo<SameEntity>`, which builds untracked instances.

### No change to the origin proof

`IsSelfComposition` accepts only element-preserving operators, so `query = query.ProjectTo<Dto>(…)` disqualifies the local, and its element type changes anyway. No code change is needed. A test pins it.

## Risks / Trade-offs

- **[Risk] EFD005 reports many more findings in AutoMapper-heavy code (Kavita).** → That is the recall the change is for. EFD005 is advisory, and its existing bounds (`Take`, key equality, per-parent filters) apply unchanged before or after `ProjectTo`. The corpus re-run is a release gate: triage every new finding, and fix any false-positive class before shipping.
- **[Risk] A `Where` after `ProjectTo` filters DTO members.** → Bound classification treats DTO members like `Select` result members, as it does today. Keys are recognized only by name there, so the bound is medium at most.
- **[Risk] Another library places a `ProjectTo` in a namespace named `AutoMapper`.** → It would have to have the same shape, an `IQueryable` source and an `IQueryable<T>` result, so following it is still a query composition.
- **[Trade-off] EFD006 still reports sibling includes before `ProjectTo` without a split mode.** → That is today's behavior for `Select`, and it's kept for consistency. Whether EF Core uses those includes depends on the mapping, which the analyzer can't see.
