# Proposal

## Why

Round-1 corpus validation (`validation/FINDINGS.md`, defects 1–3) found that EFD005 cannot be measured yet. Three causes produce most of its roughly 200 untriaged findings:

1. **`Guid` key equality is not recognized.** `x.Id == id` on a `Guid` key goes through `Guid`'s own `==` operator. The bound model accepts only built-in operators, so the query is classified as unbounded. This showed up in eShop and Jellyfin, and probably accounts for most of Bitwarden's findings.
2. **`.Equals()` key equality is not recognized.** `e.ItemId.Equals(itemId)` means the same as `==`, but it isn't treated as a bound. Jellyfin writes equality this way throughout.
3. **Key-by-name bounds are reported at medium confidence.** An equality on a property whose key status comes only from its name (`Id`, `<Entity>Id`, `*Id`) is usually a primary-key lookup or a "load the children of this parent" query. EFD005's own rule page calls the second case legitimate. At medium confidence these findings count against the precision gate. We decided on 2026-09-26 to report them as advisory only.

## What Changes

- Accept a user-defined equality operator as key equality when both of its parameters have the key property's type, ignoring nullability. This covers `Guid`, `DateTimeOffset`, and similar value types, and applies to both the key-equality predicate and the membership form `ids.Any(id => id == x.Fk)`.
- Accept a single-argument instance `Equals` call as key equality when one side is an entity key path over the `Where` parameter and the other side is a bound scalar. Both `x.Key.Equals(value)` and `value.Equals(x.Key)` count.
- Report a finding whose strongest bound is a key-by-name equality at **advisory** confidence (Info severity) instead of medium. The finding is still reported. Metadata-proven keys stay strong and suppress the finding, and time-window bounds stay advisory.
- The shared EFD004 scalar predicate model is unchanged.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd005-unbounded-query-materialization`:
  - "Recognize predicate-derived row bounds" adds the equality forms and makes the key-by-name scenario advisory.
  - "Assign EFD005 confidence by blast radius" maps a medium-strength bound to advisory confidence.

## Impact

- **Analyzer:**
  - `EfQueryOperationAnalysis`: the EFD005 bound classification only.
  - `UnboundedQueryMaterializationAnalyzer`: the confidence mapping.
- **Tests:**
  - EFD005 analyzer tests gain new fixtures.
  - Every EFD005 scenario is tagged with a `Spec` trait, since the spec is changing.
  - The EFD005 end-to-end confidence counts change.
- **Docs:** `docs/rules/EFD005.md`, the README EFD005 boundary, and `CHANGELOG.md` (Unreleased).
- **Validation:** re-run the corpus and triage the deferred EFD005 findings.
- No CLI, JSON schema, or other-rule behavior changes.
