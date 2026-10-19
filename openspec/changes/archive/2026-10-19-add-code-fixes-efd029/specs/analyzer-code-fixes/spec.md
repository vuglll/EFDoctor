## Purpose

Define the code fixes that ship in the `EFDoctor.Analyzers` package: which diagnostics they fix, the code each fix produces, and when a fix is withheld because its result would not compile or would change behavior.

## ADDED Requirements

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
