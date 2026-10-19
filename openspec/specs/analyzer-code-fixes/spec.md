# analyzer-code-fixes Specification

## Purpose
Define the code fixes that ship in the `EFDoctor.Analyzers` package: which diagnostics they fix, the code each fix produces, and when a fix is withheld because its result would not compile or would change behavior.

## Requirements

### Requirement: Ship code fixes in a separate assembly
The repository SHALL build the code fixes into an `EFDoctor.CodeFixes` assembly. It targets `netstandard2.0`, and compiles against the same Roslyn baseline as the analyzers. It SHALL NOT reference the analyzer assembly. Each fix SHALL declare the diagnostic IDs it fixes, provide a `FixAllProvider`, and offer one code action whose title describes the change.

#### Scenario: Fix offered in the IDE
- **WHEN** the IDE shows an EFDoctor diagnostic that has a code fix, and the fix's conditions hold
- **THEN** the fix appears as a quick action with a title that describes the change, and applying it changes only the reported expression

### Requirement: Replace a discarding OrderBy with ThenBy
The EFD029 fix SHALL replace the reported `OrderBy` with `ThenBy`, and the reported `OrderByDescending` with `ThenByDescending`, in reduced extension syntax and in static `Queryable.OrderBy(...)` syntax, keeping the arguments and trivia. It SHALL be offered only when the reported call's source expression has a type that is, or implements, `System.Linq.IOrderedQueryable<T>`.

#### Scenario: OrderBy after OrderBy
- **WHEN** EFD029 reports `.OrderBy(o => o.Id)` in `query.OrderBy(o => o.Name).OrderBy(o => o.Id)`
- **THEN** the fix produces `query.OrderBy(o => o.Name).ThenBy(o => o.Id)`

#### Scenario: Descending and static forms
- **WHEN** EFD029 reports `OrderByDescending(...)` after an ordering, or `Queryable.OrderBy(ordered, key)` in static form
- **THEN** the fix produces `ThenByDescending(...)` or `Queryable.ThenBy(ordered, key)`

#### Scenario: Unordered receiver
- **WHEN** a `Where` or another operator between the two orderings makes the receiver a plain `IQueryable<T>`
- **THEN** the fix is not offered, because `ThenBy` would not compile

### Requirement: Replace a count existence test with Any
The EFD002 fix SHALL rewrite the comparison of a reported `Count` or `CountAsync` invocation with an integral constant:
- `> 0`, `!= 0`, and `>= 1` SHALL become a call to `Any` or `AnyAsync`;
- `== 0`, `<= 0`, and `< 1` SHALL become the logical negation of that call.

Either operand order SHALL be accepted. The fix SHALL keep the call's arguments (the predicate and the cancellation token), its receiver, its reduced or static form, and an enclosing `await`, and SHALL replace the whole comparison. It SHALL NOT be offered when the reported invocation is not directly compared with a constant.

#### Scenario: Count compared for existence
- **WHEN** EFD002 reports `db.Orders.Count(o => o.Open)` in `db.Orders.Count(o => o.Open) > 0`
- **THEN** the fix produces `db.Orders.Any(o => o.Open)`

#### Scenario: Emptiness test and reversed operands
- **WHEN** the comparison is `db.Orders.Count() == 0`, or `0 < db.Orders.Count()`
- **THEN** the fix produces `!db.Orders.Any()`, or `db.Orders.Any()`

#### Scenario: Awaited CountAsync
- **WHEN** the comparison is `await db.Orders.CountAsync(o => o.Open, token) >= 1`, or `await db.Orders.CountAsync() < 1`
- **THEN** the fix produces `await db.Orders.AnyAsync(o => o.Open, token)`, or `!await db.Orders.AnyAsync()`

#### Scenario: Stored count or null-checked FirstOrDefault
- **WHEN** EFD002 reports a count stored in a local, or a `FirstOrDefault` checked for `null`
- **THEN** the fix is not offered
