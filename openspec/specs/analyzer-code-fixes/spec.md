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

### Requirement: Move a client-side reduction into the query
The EFD019 fix SHALL remove a reported materializer whose result is directly reduced, and apply the reduction to the query instead:
- **Synchronous:** for `ToList` or `ToArray`, the reducer call `source.ToList().Reducer(args)` SHALL become `source.Reducer(args)`. The `Count` and `Length` properties SHALL become `source.Count()`.
- **Awaited:** for `ToListAsync` or `ToArrayAsync`, the parenthesized await `(await source.ToListAsync(token)).Reducer(args)` SHALL become `await source.ReducerAsync(args, token)`. The `Count` and `Length` properties SHALL become `await source.CountAsync(token)`. A cancellation token passed to the materializer SHALL be passed to the async reducer.

The fix SHALL NOT be offered when the materialized result is stored in a local, or passed to a static `Enumerable` call.

#### Scenario: Synchronous reduction
- **WHEN** EFD019 reports `ToList()` in `db.Orders.ToList().First(o => o.Open)`, or `db.Orders.ToArray().Length`
- **THEN** the fix produces `db.Orders.First(o => o.Open)`, or `db.Orders.Count()`

#### Scenario: Awaited reduction with a token
- **WHEN** EFD019 reports `ToListAsync(token)` in `(await db.Orders.ToListAsync(token)).Any(o => o.Open)`, or in `(await db.Orders.ToListAsync()).Count`
- **THEN** the fix produces `await db.Orders.AnyAsync(o => o.Open, token)`, or `await db.Orders.CountAsync()`

#### Scenario: Stored or statically reduced result
- **WHEN** EFD019 reports a materialization stored in a local, or passed to `Enumerable.Count(...)` in static form
- **THEN** the fix is not offered

### Requirement: Await the async counterpart of a synchronous call
The EFD010 fix SHALL replace the reported synchronous call with an `await` of the counterpart that EFD010 names. It SHALL:
- rename the method to the counterpart, and keep its arguments;
- wrap the call in `await`, parenthesized when the call's result is the receiver of a member access or a further invocation;
- add `using Microsoft.EntityFrameworkCore;` when the counterpart is declared on `EntityFrameworkQueryableExtensions` and the document lacks that directive;
- append a `CancellationToken` parameter that is in scope (of the enclosing method, lambda, or local function) as the last argument, except for `FindAsync`.

The fix SHALL be offered only when all of these hold:
- the nearest enclosing method, lambda, or local function is declared `async`;
- the call isn't inside a `lock` statement;
- the call is in reduced extension or instance form;
- a method with the counterpart's name exists on the call's resolved type, or on `EntityFrameworkQueryableExtensions`.

#### Scenario: Query terminal in an async method
- **WHEN** EFD010 reports `db.Orders.Where(o => o.Open).ToList()` in an async method without a token
- **THEN** the fix produces `await db.Orders.Where(o => o.Open).ToListAsync()`, and adds `using Microsoft.EntityFrameworkCore;` if it is missing

#### Scenario: SaveChanges, Find, and a token in scope
- **WHEN** EFD010 reports `db.SaveChanges()` or `db.Orders.Find(id)` in an async method with a `CancellationToken ct` parameter
- **THEN** the fix produces `await db.SaveChangesAsync(ct)`, or `await db.Orders.FindAsync(id)`

#### Scenario: Member read from the result
- **WHEN** EFD010 reports `db.Orders.ToList()` in `db.Orders.ToList().Count`
- **THEN** the fix produces `(await db.Orders.ToListAsync()).Count`

#### Scenario: Await not possible
- **WHEN** the reported call is inside a `lock` statement, or in static form such as `Queryable.Count(db.Orders)`
- **THEN** the fix is not offered
