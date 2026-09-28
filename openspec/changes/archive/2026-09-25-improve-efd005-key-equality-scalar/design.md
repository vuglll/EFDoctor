# Design

## Context

See `proposal.md` for motivation and `specs/efd005-unbounded-query-materialization/spec.md` for the refined behavior contract. This change refines the bound model introduced by the archived `improve-efd005-bound-model`, which added a `RowBound` (kind + strength) to `EfQueryChainAnalysis` and classifies `Queryable.Where` predicates through `ClassifyWhereBound` → `ClassifyPredicateBound` → `TryGetComparedEntityProperty` → `IsBoundScalar` in the shared `EfQueryOperationAnalysis`.

A real-project run against the private codebase produced two EFD005 false positives from one root cause: in a key-equality predicate `x.Key == value`, `IsBoundScalar` accepted the compared `value` only when it was a constant, local, non-entity parameter, field, or default-value operation. It rejected a property or member access on another object (for example `product.ProductId`), so `x.ProductId == product.ProductId` was not recognized as key-equality and the query was classified `None` and reported. Comparing a foreign key to a property of a parent entity in hand is the canonical "hydrate the children of this one parent" shape, so this gap re-introduces exactly the false-positive class the base change targets.

## Goals / Non-Goals

**Goals:**

- Accept a property/member access whose root is not the `Where` entity parameter as the compared scalar in an EFD005 key-equality bound, so `x.Fk == other.Property` establishes `KeyEquality`/`KeyEqualityByName` just as `x.Fk == local` does.
- Reject a comparand that is itself a property path over the `Where` entity parameter, so `x.A == x.B` establishes no key-equality bound.
- Keep the refinement confined to `IsBoundScalar`; leave the shared EFD004 `IsScalar` model and all EFD004 fixtures unchanged.
- Prove the two private-codebase false positives are gone without any previously suppressed query starting to report.

**Non-Goals:**

- Broadening membership (`Contains`/`Any`), time-window, or any non-key bound recognition.
- Following the compared property through data flow to prove it resolves to a single row; a property access is treated as a single scalar comparand by shape, matching how a local or field is treated.
- Changing confidence assignment, severity mapping, the test-project downgrade, the JSON schema, or any other rule.
- Widening EFD004's `IsScalar`. EFD004 asks a different question (is this expression server-translatable), and its scalar model must not change here.

## Decisions

### 1. Make `IsBoundScalar` self-contained instead of delegating to the shared `IsScalar`

The base `IsBoundScalar` delegated to the shared `IsScalar` (which also serves EFD004) plus a field-reference case. `IsScalar` accepts an entity property path over the entity parameter as scalar — correct for EFD004's server-translatability question, but wrong for an EFD005 key-equality comparand, where an entity-side property is not a single external value. Rather than widen the shared `IsScalar` (which would risk EFD004 fixtures), `IsBoundScalar` is rewritten to be self-contained:

- Reject the comparand when it is an entity property path over the `Where` parameter (`IsEntityPropertyPath`), so `x.A == x.B` is not a bound.
- Accept a constant, local, non-entity parameter, default expression, field, **or** any `IPropertyReferenceOperation` (a property/member access) — the last being the new acceptance that covers `product.ProductId`, `parent.Id`, and nested `request.Filter.OwnerId`.

Only the top-level operation kind is inspected for the comparand, so a nested member access is accepted without walking or validating its receiver — consistent with the design's no-data-flow rule.

Alternative considered: add a property-access clause on top of the existing `IsScalar` delegation. Rejected because it would leave `x.A == x.B` classified as a bound (via `IsScalar`'s entity-property-path branch), contradicting the "exactly one side is an entity key path" contract.

Alternative considered: widen the shared `IsScalar` to accept foreign member access. Rejected — it changes EFD004's predicate acceptance and risks its fixtures, violating the "EFD004 behavior unchanged" gate.

### 2. Treat a foreign property access as a single scalar comparand by shape

A property access such as `product.ProductId` is treated as one scalar value for bound purposes, exactly as a local or field is. EFD005 does not attempt to prove that `product` resolves to a single row or that the property is itself a key; the bound derives from `x.Key == <single value>`, and the entity-side key already guarantees at most one matching row per compared value. This keeps the traversal linear and free of data flow.

### 3. Strength follows the entity-side key, not the comparand

Whether the bound is `KeyEquality` (strong, suppress) or `KeyEqualityByName` (medium, downgrade) is decided entirely by how the entity-side property's key status is proven — EF metadata versus name heuristic — unchanged from the base. The comparand kind (constant, local, or foreign property) never affects strength.

## Risks / Trade-offs

- **[A foreign property comparand might not resolve to a single value]** → The entity-side key path guarantees at most one matching row per distinct compared value, so treating the comparand as a single scalar is sound regardless of what `product` is; a multi-valued receiver is a different predicate shape (membership) handled by the existing `Contains`/`Any` path.
- **[Rewriting `IsBoundScalar` could regress the base]** → All existing EFD005 fixtures (including metadata-key, FK, alternate-key, name-heuristic, and time-window cases) and all EFD004 fixtures are kept green as a gate; the shared `IsScalar` is not touched.
- **[Over-broad comparand acceptance could suppress a genuinely unbounded query]** → The bound still requires exactly one side to be a proven entity key path over the `Where` parameter; a predicate with entity property paths on both sides, or with no entity key path, establishes no key-equality bound.

## Migration Plan

1. Rewrite `IsBoundScalar` per Decision 1.
2. Add analyzer fixtures for `x.Fk == other.Property` (metadata key, FK, nested member, compound `&&`, name-heuristic medium) and a positive `x.Key == x.OtherProperty` case.
3. Re-run all EFD004 and EFD005 fixtures and confirm no unexpected change.
4. Confirm the private-codebase shape is suppressed via the fixtures that mirror it.
5. Update `docs/rules/EFD005.md`, README EFD005 boundary, and `CHANGELOG.md`.

No persisted data or report migration is required. Rollback restores the delegating `IsBoundScalar`; schema-version-1 consumers are unaffected.
