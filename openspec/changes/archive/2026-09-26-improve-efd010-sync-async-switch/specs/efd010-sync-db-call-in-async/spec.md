## MODIFIED Requirements

### Requirement: Detect a synchronous EF Core database call in an async context
EFD010 SHALL report a resolved synchronous EF Core database call that occurs inside an `async` method or `async` lambda and that has a direct EF async counterpart. The following are in scope: a materializing or aggregating LINQ terminal — `ToList`, `ToArray`, `ToDictionary`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `Count`, `LongCount`, `Any`, `All`, `Min`, `Max`, `Sum`, `Average`, `Contains`, `ElementAt`, `ElementAtOrDefault` — whose source traces through supported composition to an EF Core `DbSet`; `SaveChanges` on a `DbContext`; and `Find` on a `DbSet`. Matching SHALL use resolved method and type symbols rather than name text. EFD010 SHALL anchor the finding on the synchronous call.

EFD010 SHALL NOT report a synchronous call in one arm of a conditional (a conditional expression or an `if`/`else` statement) when the other arm of the same conditional awaits the EF async counterpart that EFD010 would suggest for the call. The await may be applied to the counterpart directly or through `ConfigureAwait`. This exclusion SHALL consider only conditionals inside the same method, lambda, or local function as the call.

#### Scenario: Synchronous terminal in an async method
- **WHEN** an `async` method calls `context.Orders.Where(...).ToList()`
- **THEN** EFD010 reports the `ToList` call and names `ToListAsync` as the replacement

#### Scenario: SaveChanges in an async method
- **WHEN** an `async` method calls `context.SaveChanges()`
- **THEN** EFD010 reports the call and names `SaveChangesAsync` as the replacement

#### Scenario: Find in an async method
- **WHEN** an `async` method calls `context.Orders.Find(id)`
- **THEN** EFD010 reports the call and names `FindAsync` as the replacement

#### Scenario: Synchronous call in an async lambda
- **WHEN** a synchronous EF terminal is called inside an `async` lambda
- **THEN** EFD010 reports the call

#### Scenario: Synchronous method is not reported
- **WHEN** the enclosing method or lambda is not `async`
- **THEN** EFD010 does not report, because awaiting is not available without restructuring

#### Scenario: Already asynchronous call is not reported
- **WHEN** the code already calls the `*Async` counterpart (for example `ToListAsync`)
- **THEN** EFD010 does not report

#### Scenario: In-memory or non-EF source is not reported
- **WHEN** a terminal operates on an in-memory sequence or an `IQueryable<T>` not proven to originate from a `DbSet`
- **THEN** EFD010 does not report

#### Scenario: Sync arm of a sync/async conditional expression
- **WHEN** an `async` method evaluates `async ? await q.FirstOrDefaultAsync(p) : q.FirstOrDefault(p)`
- **THEN** EFD010 does not report the `FirstOrDefault` call, because the method already awaits the counterpart on its async path

#### Scenario: Sync branch of a sync/async if statement
- **WHEN** an `async` method calls `await context.SaveChangesAsync().ConfigureAwait(false)` in one branch of an `if`/`else` and `context.SaveChanges()` in the other
- **THEN** EFD010 does not report the `SaveChanges` call

#### Scenario: Other arm awaits something else
- **WHEN** one branch of an `if`/`else` awaits an unrelated operation and the other calls a synchronous EF terminal
- **THEN** EFD010 reports the synchronous call
