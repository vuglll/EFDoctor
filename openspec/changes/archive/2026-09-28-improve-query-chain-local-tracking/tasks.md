# Tasks

## 1. Shared helper

- [x] 1.1 Add `EfQueryLocals`. It keeps a per-operation-tree cache of each local's declaration, its writes, and whether it is followable. `TryResolve(ILocalReferenceOperation, out IOperation value)` implements the statically determined rule.
- [x] 1.2 Follow resolvable locals in `EfQueryOperationAnalysis.TryAnalyzeSourceCore`, with a hop limit of 8. Append `local '<name>'` to the operation list.

## 2. Include walkers

- [x] 2.1 EFD017: follow resolvable locals in `ProjectionDropsIncludeAnalyzer.TryAnalyzeChain`.
- [x] 2.2 EFD006: follow resolvable locals, mark includes collected through a local, and skip the report when the reported include came through a local.
- [x] 2.3 EFD025: follow resolvable locals, mark chains collected through a local, and apply the once-only rule.

## 3. Tests

- [x] 3.1 Add helper-level tests, traced to `query-chain-local-tracking`, for each scenario: stored then executed, straight-line recomposition, several locals, a conditional write, a lambda read, a ref/out/deconstruction/compound write, and evidence naming.
- [x] 3.2 Add positive local fixtures for EFD004, EFD005, EFD006, EFD009, EFD010, EFD013, EFD014, EFD017, EFD019, EFD020, EFD022, EFD023, and EFD025, and a conditional-write negative for each rule whose spec names one.
- [x] 3.3 Add EFD006 and EFD025 once-only tests, traced to the "Report include findings once" scenarios.
- [x] 3.4 Update the existing stored-local negative fixtures that become positive, and re-tag the changed EFD005, EFD006, and EFD023 scenarios.

## 4. Documentation

- [x] 4.1 Update the rule pages that describe the local boundary (EFD004, EFD005, EFD006, EFD009, EFD017, EFD023, EFD025), the README chain-proof wording, the product brief's cross-cutting list, and the CHANGELOG (Unreleased).

## 5. Validation and verification

- [x] 5.1 Build with no warnings and run the full suite.
- [x] 5.2 Re-run the whole corpus, triage every new finding, update `validation/FINDINGS.md`, and regenerate `SUMMARY.md`. Stop and investigate if a new false-positive class appears.
- [x] 5.3 Run `openspec validate improve-query-chain-local-tracking --strict`.
