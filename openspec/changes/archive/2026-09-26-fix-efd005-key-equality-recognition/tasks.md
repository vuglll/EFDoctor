# Tasks

## 1. Bound classification

- [x] 1.1 Accept a user-defined `op_Equality` whose parameter types match the entity key property's type, ignoring nullability, in EFD005 key-equality classification. Verify with new `Guid` and `Guid?` fixtures: a metadata key is not reported, and a name-heuristic key is reported at advisory confidence.
- [x] 1.2 Accept a same-typed user-defined `op_Equality` in the `Any` membership lambda. Verify with a `Guid[] ids` `Any` fixture that is not reported.
- [x] 1.3 Recognize a single-argument instance `Equals` as key equality in both receiver orders. Reject it when both sides are entity property paths. Verify with fixtures for `x.MetadataKey.Equals(id)` and `id.Equals(x.MetadataKey)` (both not reported), `x.ProductId.Equals(id)` (reported at advisory), and `x.MetadataKey.Equals(x.ProductId)` (reported).
- [x] 1.4 Confirm the shared EFD004 `IsPredicateExpression`/`IsScalar` are unchanged. Verify that the EFD004 test suite passes unmodified.

## 2. Confidence

- [x] 2.1 Map medium-strength bounds to `advisory` in `UnboundedQueryMaterializationAnalyzer.GetConfidence`, including in hot-path call sites. Verify with updated `AssignsConfidenceFromBoundAndCallSite` cases, plus a new hot-path key-name case.
- [x] 2.2 Update the EFD005 end-to-end expectations to 2 medium and 2 advisory findings. Verify that `Efd005FixtureProducesBoundAwareNonDuplicateConsoleAndJsonResults` passes.
- [x] 2.3 Make sure the analyzer `description:` does not contradict the spec.

## 3. Spec tracing

- [x] 3.1 Tag the EFD005 tests with `[Trait("Spec", "efd005-unbounded-query-materialization/<scenario>")]` so that every scenario in the synced spec is covered. Verify that `RepositoryConsistencyTests` passes after the sync.

## 4. Documentation

- [x] 4.1 Update `docs/rules/EFD005.md`: the confidence line, the key-name bound (now advisory), and the recognized equality forms (`Guid`/user-defined `==`, `.Equals()`).
- [x] 4.2 Update the README EFD005 boundary section to match.
- [x] 4.3 Add a `CHANGELOG.md` entry under **Unreleased**.

## 5. Verification and revalidation

- [x] 5.1 Build the solution with no warnings or errors, and run the full test suite.
- [x] 5.2 Re-run the corpus for eshop, jellyfin, bitwarden, and smartstore. Confirm that the Guid/`.Equals()` findings listed in `validation/FINDINGS.md` are gone, and that key-name findings are now advisory. Regenerate `SUMMARY.md` and update the EFD005 status in `FINDINGS.md`.
- [x] 5.3 Run `openspec validate fix-efd005-key-equality-recognition --strict`.
