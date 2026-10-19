## ADDED Requirements

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
- **WHEN** the reported call is in a non-async lambda inside an async method, inside a `lock` statement, or in static form
- **THEN** the fix is not offered
