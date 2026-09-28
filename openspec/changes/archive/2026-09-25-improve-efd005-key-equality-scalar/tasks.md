# Tasks

## 1. Extend EFD005 bound-scalar acceptance (S1, S2)

- [x] 1.1 Rewrite `EfQueryOperationAnalysis.IsBoundScalar` to accept a property/member access (`IPropertyReferenceOperation`) whose root is not the `Where` entity parameter as a valid compared scalar, in addition to the existing constant, local, non-entity parameter, field, and default-value operations.
- [x] 1.2 Reject a comparand that is an entity property path over the `Where` parameter so `x.A == x.B` establishes no key-equality bound; ensure classification still requires exactly one side to be an entity key path over the `Where` parameter.
- [x] 1.3 Confirm strength is unchanged: metadata-proven key → `KeyEquality`/`Strong` (suppress); name-heuristic key → `KeyEqualityByName`/`Medium` (downgrade); the comparand kind never affects strength.

## 2. Keep the change EFD005-local (S2)

- [x] 2.1 Confirm the shared EFD004 `IsScalar` is not modified; the new acceptance lives only in `IsBoundScalar`.
- [x] 2.2 Re-run all existing EFD004 positive and negative fixtures and verify no finding count, location, or evidence change. (Full suite: 827 analyzer + 52 CLI tests pass.)

## 3. Analyzer fixtures

- [x] 3.1 Negative: `Where(entity => entity.MetadataKey == other.MetadataKey)` (metadata key, property comparand) is not reported.
- [x] 3.2 Negative: `Where(entity => entity.ParentKey == other.ParentKey)` (foreign key, property comparand) is not reported.
- [x] 3.3 Negative: compound `Where(entity => entity.ParentKey == product.ParentKey && entity.Active)` mirroring the private-codebase shape is not reported.
- [x] 3.4 Negative: nested member `Where(entity => entity.MetadataKey == request.Filter.MetadataKey)` is not reported.
- [x] 3.5 Positive (medium): name-heuristic key with a property comparand `Where(entity => entity.ProductId == product.ProductId)` still reports at `KeyEqualityByName (Medium)` confidence, not suppressed.
- [x] 3.6 Positive: both-sides-entity `Where(entity => entity.MetadataKey == entity.ProductId)` establishes no bound and reports.

## 4. Real-project revalidation

- [x] 4.1 Reproduce the private-codebase shape via fixtures 3.3/3.5 (`x.Fk == product.Fk` with and without a trailing `OrderBy`/conjunction) and confirm the strong-key form is suppressed and the name-heuristic form downgrades rather than reporting `None`. Note: the private codebase is external to this repo; the two originating findings are FK key-equality against `product.ProductId` and are now recognized as strong key bounds.
- [x] 4.2 Verify no previously suppressed EFD005 fixture now reports and no new EFD005 findings appear as a result of this change.

## 5. Documentation

- [x] 5.1 Update `docs/rules/EFD005.md` so the key-equality bound description and examples state that comparing an entity key to a property of another object (for example `x.Fk == parent.Id`) is a recognized bound.
- [x] 5.2 Update the README EFD005 boundary section to match, keeping wording consistent with the rule doc and fixtures.
- [x] 5.3 Add a `CHANGELOG.md` entry under **Unreleased**.

## 6. Final verification

- [x] 6.1 Build the solution from a clean state on .NET 10 and verify no errors or warnings.
- [x] 6.2 Run the full automated suite and verify all tests pass, with EFD004 and EFD006 unchanged.
- [x] 6.3 Run strict OpenSpec validation for `improve-efd005-key-equality-scalar`.
