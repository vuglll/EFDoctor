# Tasks

## 1. Batch-loop recognition

- [x] 1.1 Add the batch iteration variable check for `foreach`/`await foreach`, using the element type from `GetForEachStatementInfo`. Verify with the chunk fixture, which is not reported.
- [x] 1.2 Add the check for a page declared by the loop condition (`out var`, pattern designation, assignment). Verify with the pager fixture, which is not reported.
- [x] 1.3 Add the check for a page loaded in the loop body, for `while`, `do`, and `for` loops. Verify with the `while (true)` paged-list fixture, which is not reported. The `foreach` per-order-with-lines fixture must still be reported.
- [x] 1.4 Add the threshold-flush check: an incremented local, a `%` expression over one, or `Count`/`Length`. Verify with the counter, modulo, and buffer fixtures, which are not reported, and the `if (i > 0)` and `if (item.IsTransient)` fixtures, which are still reported.
- [x] 1.5 Update the analyzer `description:` so it doesn't contradict the spec.

## 2. Tests and tracing

- [x] 2.1 Add a fixture where a per-item loop nested in a chunk loop is still reported.
- [x] 2.2 Tag every `efd001-detection` scenario with a `Spec` trait, including the existing ones. Verify that `RepositoryConsistencyTests` passes after the sync.

## 3. Documentation

- [x] 3.1 Update `docs/rules/EFD001.md`, the README EFD001 boundary, and the CHANGELOG (Unreleased).

## 4. Verification

- [x] 4.1 Build with no warnings and run the full test suite.
- [x] 4.2 Re-run the corpus for Jellyfin and Smartstore. Confirm that the 24 batch false positives are gone, and that the true positive and the per-item `acceptable` findings remain. Update `validation/FINDINGS.md` and regenerate `SUMMARY.md`.
- [x] 4.3 Run `openspec validate improve-efd001-batch-saves --strict`.
