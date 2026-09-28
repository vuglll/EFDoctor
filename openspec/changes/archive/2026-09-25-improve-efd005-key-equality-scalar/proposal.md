# Proposal

## Why

Validating EFD005 against a private real-world codebase surfaced two false positives, both from EFD005 (Query materialization has no recognized row bound) with a single shared root cause:

- `Where(x => x.ParentId == parent.ParentId && x.ActionName == ActionNames.Submit)`
- `Where(x => x.ParentId == parent.ParentId).OrderBy(...)`

Both queries are bounded by a key-equality predicate `x.Fk == <single value>`, yet EFD005 classifies them as bound `None` and reports. The value on the right of the equality is a property access on another object (`parent.ParentId`), and the EFD005 bound model does not recognize a property/member access as the compared scalar value — `IsBoundScalar` accepted only constants, locals, non-entity parameters, fields, and default-value operations. So `x.ParentId == parent.ParentId` was not recognized as key-equality and fell through to `None`.

Comparing a foreign key to a property of a parent entity already in hand is the single most common way to express "load the children of this one parent." Leaving it unrecognized re-introduces exactly the false-positive class the `improve-efd005-bound-model` change set out to eliminate.

This change is a narrow, EFD005-local refinement of the bound model. It does not change EFD004, EFD006, the CLI pipeline, the confidence gradient, the JSON schema, or any other rule.

## What Changes

- **S1 — Recognize a property/member access as the compared scalar in a key-equality bound.** Extend the EFD005-specific `IsBoundScalar` (reached via `ClassifyPredicateBound` → `TryGetComparedEntityProperty` in `EfQueryOperationAnalysis`) so the value compared against an entity key may be a property or member access whose root is not the `Where` entity parameter — for example `product.ProductId`, `parent.Id`, or `request.Filter.OwnerId`. When exactly one side of the equality is an entity key path over the `Where` parameter and the other side is such a non-entity scalar value, the predicate is recognized as `KeyEquality` (metadata-proven) or `KeyEqualityByName` (name-heuristic), exactly as for a constant or local.
- **S2 — Reject a comparand that is itself an entity property path.** A property path over the `Where` entity parameter is the entity side of the comparison, not a comparand: `x.A == x.B` (both entity properties) does not bound to a single row, so it establishes no key-equality bound. This keeps the refinement EFD005-local and MUST NOT alter the shared EFD004 `IsScalar` predicate model or any EFD004 fixture.

## Capabilities

### Modified Capabilities

- `efd005-unbounded-query-materialization`: Refines the "Recognize predicate-derived row bounds" requirement so the scalar value compared against an entity key in a key-equality predicate may be a property or member access on another object, and so a comparand that is itself an entity property path over the `Where` parameter establishes no key-equality bound.

### New Capabilities

None. This change refines a single existing requirement and reuses the analyzer, finding, suppression, CLI, and reporting contracts unchanged.

## Impact

- Builds on the archived `improve-efd005-bound-model`, which introduced the `RowBound` model, `ClassifyPredicateBound`, `TryGetComparedEntityProperty`, and `IsBoundScalar` in the shared `EfQueryOperationAnalysis`.
- Edits `EfQueryOperationAnalysis.IsBoundScalar` only; the shared `IsScalar` used by EFD004 is untouched.
- Adds analyzer fixtures for `x.Fk == other.Property` (metadata key, FK, nested member, compound `&&`, and name-heuristic medium), plus a positive fixture confirming `x.Key == x.OtherProperty` (both-sides-entity) still reports.
- Confirms the private-codebase false positives are gone with no previously suppressed query starting to report.
- Updates `docs/rules/EFD005.md`, the README EFD005 boundary, and `CHANGELOG.md`.
- Introduces no network calls, telemetry, code fixes, database connectivity, CLI surface, or JSON schema-version change. EFD004 and EFD006 behavior is unchanged.
