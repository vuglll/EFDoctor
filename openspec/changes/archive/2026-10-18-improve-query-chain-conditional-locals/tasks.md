# Tasks

## 1. Shared proof

- [x] 1.1 Split `EfQueryLocals.LocalWrites` into unsafe and not-straight-line disqualification. Record the initializer and every plain assignment statement's value anywhere in the body, and expose them through a `TryGetComposedWrites` accessor. Verify that the existing `EfQueryLocalsTests` still pass.
- [x] 1.2 Add a self-composition check and the origin hop, behind an optional `followComposedLocals` parameter, in `EfQueryOperationAnalysis.TryAnalyzeSource`. The hop appends `local '<name>' (conditionally composed)`. Verify with new `EfQueryLocalsTests` for each origin-proof scenario in `query-chain-local-tracking`:
  - a branch, a loop, and a conditional expression;
  - a read inside a branch;
  - a replacement from another query;
  - `Select` and `Concat`;
  - `ref` and expression assignment;
  - a disabled flag.
- [x] 1.3 Add a `followComposedLocals` parameter to `EfPredicateScan.Create`, and verify that it builds.

## 2. Opt the rules in

- [x] 2.1 Pass `followComposedLocals: true` in EFD004, EFD010, EFD013, EFD019, and EFD022, and in EFD009 and EFD023 through `EfPredicateScan`. Verify that each rule has a new positive conditional-local fixture and a negative replaced-local fixture, and that both pass.
- [x] 2.2 Add an EFD019 fixture: unordered `Last` after a conditional `OrderBy`, which isn't reported. Add EFD005 and EFD014 fixtures: a conditionally composed local, which isn't reported. Verify that they pass.
- [x] 2.3 Tag every new and modified scenario with `[Trait("Spec", ...)]`. Verify that `RepositoryConsistencyTests` and the full `dotnet test` pass.

## 3. Docs and validation

- [x] 3.1 Update the docs:
  - the roadmap's cross-cutting list;
  - the rule pages for EFD004, EFD009, and EFD023, and the README chain-proof wording, where they describe the local boundary;
  - the CHANGELOG's Unreleased section.
  
  Verify that `RepositoryConsistencyTests` pass.
- [x] 3.2 Re-run the corpus with `python3 validation/run_corpus.py`. Triage every new finding in `validation/results/triage/`, and record the new findings and their verdicts in `validation/FINDINGS.md`. Verify that no new finding is `FP`, or fix the cause and re-run.
- [x] 3.3 Run `openspec validate improve-query-chain-conditional-locals --strict` and the privacy denylist grep. Verify that both are clean.
