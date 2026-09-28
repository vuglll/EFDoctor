# Design

## Context

EFD005 classifies each `Where` predicate into a `RowBound`, using `EfQueryOperationAnalysis.ClassifyPredicateBound`. Key equality is recognized only for an `IBinaryOperation` whose `OperatorMethod is null`, which Roslyn reports only for built-in operators. `Guid`, `DateTimeOffset`, and similar types declare `op_Equality`, so their comparisons carry a non-null `OperatorMethod` and fall through to `None`. The membership check `IsLocalCollectionMembership` has the same `OperatorMethod is not null` rejection in its `Any` lambda. `.Equals()` is an `IInvocationOperation`, which the classifier never inspects for equality.

The shared `IsPredicateExpression`/`IsScalar` model is used by EFD004. It also rejects user-defined operators. It must stay unchanged, as the existing spec requires.

## Goals / Non-Goals

**Goals:**
- Recognize the key-equality forms, and apply the advisory downgrade, inside EFD005's own classification.

**Non-Goals:**
- Widening EFD004's scalar model.
- Recognizing static `object.Equals(a, b)`, `EqualityComparer<T>.Default.Equals`, or `Equals` inside an `Any` lambda. None of these appeared in the corpus, so they stay unrecognized for now.
- Changing bound *strength* semantics. Metadata-proven keys stay strong and name-heuristic keys stay medium.

## Decisions

### Accepting a user-defined equality operator

**Decision:** Accept it when all of these hold:
- the operator is named `op_Equality`;
- it has two parameters;
- both parameter types, after unwrapping `Nullable<T>`, equal the entity property's type, also after unwrapping.

For the `Any` membership lambda, accept it when both parameters have the same type.

**Why:** Tying the operator to the key's type rules out overloads that don't express value equality of the key, such as an operator between a key and some wrapper type. Lifted nullable comparisons report the underlying `op_Equality` in `OperatorMethod`, with `IsLifted`, so unwrapping covers `Guid?`.

**Alternative:** Any non-null `OperatorMethod`. Rejected, because it is too permissive for an analyzer that suppresses findings.

### Accepting `.Equals()`

**Decision:** Accept an `IInvocationOperation` when all of these hold:
- its method is named `Equals`;
- it is an instance, non-static method;
- it has one parameter;
- it returns `bool`.

The receiver and the argument are matched exactly like the two operands of `==`: one side is an entity property path and the other is a bound scalar. The argument is unwrapped from its boxing conversion when `Equals(object)` is chosen.

**Why:** EF Core translates both `IEquatable<T>.Equals(T)` and `object.Equals(object)` to SQL `=`. The existing `TryGetComparedEntityProperty` logic is reused through a small operand-pair helper.

### Advisory confidence for medium bounds

**Decision:** `GetConfidence` returns `advisory` for both `Medium` and `Weak` strength. The strength value and the evidence text (`KeyEqualityByName (Medium)`) stay the same, so evidence still distinguishes a key-name bound from a time window.

**Alternative:** Reclassifying `KeyEqualityByName` as `Weak`. Rejected, because evidence and the `Stronger` ordering would then treat it as equivalent to a time window, and a key-name bound is the stronger signal.

## Risks / Trade-offs

- **[Risk]** A `Guid` column that is not actually a key but ends in `Id` becomes advisory instead of medium. → This is accepted by the 2026-09-26 decision. It is still reported.
- **[Risk]** Recognizing `.Equals()` could suppress findings for a metadata key whose type has a nonstandard `Equals`. → EF translates only well-known `Equals` forms, and an untranslatable one fails at runtime anyway, so no new false negatives are observable.

## Migration Plan

This is a confidence change for existing users. Some findings that were Warning become Info. Record it in the CHANGELOG under Unreleased.
