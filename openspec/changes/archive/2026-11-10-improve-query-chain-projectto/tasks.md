# Tasks

## 1. Shared proof

- [x] 1.1 Add `EfQueryOperationAnalysis.TryGetProjectToSource` and `ProjectToOperation`. Follow `ProjectTo` in `TryAnalyzeSourceCore`, recording `AutoMapper.ProjectTo` with no row bound. Verify with `EfQueryLocalsTests` for the extension and mapper forms, a stored query, an unrelated `ProjectTo`, and a conditional `ProjectTo` write that the origin proof doesn't follow.
- [x] 1.2 Add an AutoMapper stub (`AutoMapper.IConfigurationProvider`, `AutoMapper.IMapper.ProjectTo`, `AutoMapper.QueryableExtensions.Extensions.ProjectTo`) for test sources, and verify that it compiles with no AutoMapper package reference.

## 2. Rules

- [x] 2.1 Make EFD006's include-chain walk continue through `ProjectTo`, in `TryAnalyzeChain` and in the outermost-call check. Verify with fixtures: `AsSplitQuery`/`AsSingleQuery` after `ProjectTo` isn't reported, and sibling includes before `ProjectTo` with no split mode are reported once.
- [x] 2.2 Add EFD005 fixtures: an unbounded `ProjectTo(...).ToListAsync()` is reported, and a `Take` or key-equality bound before or after `ProjectTo` isn't.
- [x] 2.3 Treat `AutoMapper.ProjectTo` as a projection in EFD002, EFD026, EFD032, and EFD038. Verify with a negative fixture for each, using `ProjectTo` to the entity type itself.
- [x] 2.4 Tag every new scenario with `[Trait("Spec", ...)]`. Verify that `RepositoryConsistencyTests` and the full `dotnet test` pass.

## 3. Docs and validation

- [x] 3.1 Add the change to the CHANGELOG's Unreleased section, and update the README chain-proof wording if it lists what the proof follows. Verify that `RepositoryConsistencyTests` pass.
- [x] 3.2 Re-run the corpus, and compare it with a baseline run of `dev`. Triage every new or removed finding, and record the results in `validation/FINDINGS.md`. Verify that no new finding is `FP`, or fix the cause and re-run.
- [x] 3.3 Run `openspec validate improve-query-chain-projectto --strict`, and verify that it is clean.

## Workflow follow-up

- Archive the change with `/opsx:archive`.
- Push a branch, open a PR into `dev`, and merge it when CI is green.
