# Spec Delta

## ADDED Requirements

### Requirement: Recommend an existence query when a count only tests existence
When a count reducer's result (`Count`, `LongCount`, `List<T>.Count`, or array `Length`) is compared with a constant in one of the forms `> 0`, `!= 0`, `>= 1`, `== 0`, `<= 0`, or `< 1`, in either operand order, EFD019 SHALL recommend `Any` or its EF Core asynchronous counterpart instead of `Count`, as a negation for the empty-set forms, and the evidence SHALL name the comparison. A predicate passed to the count reducer SHALL be kept as the `Any` predicate. Other uses of the count SHALL keep the `Count` recommendation.

#### Scenario: Positive existence comparison
- **WHEN** `context.Orders.ToList().Count > 0` is analyzed
- **THEN** EFD019 reports, the remediation names `Any`, and it does not name `Count` as the replacement

#### Scenario: Empty-set comparison
- **WHEN** `(await context.Orders.ToListAsync()).Count == 0` is analyzed
- **THEN** the remediation names the negation of `AnyAsync`

#### Scenario: Count used as a value
- **WHEN** `context.Orders.ToList().Count` is returned or compared with another number
- **THEN** the remediation names `Count`

### Requirement: Detect a stored result that is used only for its count
EFD019 SHALL report a materializer whose value, after any `await`, initializes a local declared by a declaration statement, when the method references that local at least once and every reference is the instance of `List<T>.Count` or an array's `Length`, or the source of `Enumerable.Any`, `Enumerable.Count`, or `Enumerable.LongCount` with no predicate, outside any lambda or local function. It SHALL recommend `Any` when every reference is `Any()` or a count in an existence comparison, and `Count` otherwise, and SHALL say to run the query-side operator once when the local is read more than once. The evidence SHALL name the local.

#### Scenario: Stored list tested for existence
- **WHEN** `var products = db.Products.ToList(); return products.Count > 0;` is analyzed
- **THEN** EFD019 reports the `ToList` invocation, names local `products`, and recommends `Any`

#### Scenario: Stored list tested for emptiness
- **WHEN** the only read is `products.Count == 0`, `products.Count <= 0`, `products.Count < 1`, or `!products.Any()`
- **THEN** EFD019 reports, and recommends `Any`

#### Scenario: Stored list counted
- **WHEN** `var products = db.Products.ToList(); return products.Count;` is analyzed
- **THEN** EFD019 reports, and recommends `Count`

#### Scenario: Awaited and array forms
- **WHEN** the local is initialized by an awaited `ToListAsync` or `ToArrayAsync`, or by `ToArray`, and read through `Count`, `Length`, `Count()`, or `Any()`
- **THEN** EFD019 reports, and recommends the asynchronous counterpart for the awaited forms

#### Scenario: Several count reads
- **WHEN** the local is read as `products.Count == 0` and again as `products.Count`
- **THEN** EFD019 reports once, recommends `Count`, and says to run it once and reuse the value

#### Scenario: Rows are used
- **WHEN** the local is also enumerated, indexed, passed as an argument, returned, assigned to another variable or member, reduced with a predicate or an element reducer, or read inside a lambda or local function
- **THEN** EFD019 does not report

#### Scenario: Reassigned or unread local
- **WHEN** the local is assigned again, or is never read
- **THEN** EFD019 does not report

## MODIFIED Requirements

### Requirement: Preserve boundaries and existing rules
EFD019 SHALL identify materializers, reducers, and query origins by resolved symbols. It SHALL NOT report explicit client-evaluation boundaries such as `AsEnumerable` or `AsAsyncEnumerable`, materialized values stored in fields, properties, or parameters before reduction, materialized values stored in a local unless the local is used only for its count (see "Detect a stored result that is used only for its count"), arbitrary `IQueryable<T>` values or in-memory queryables, unresolved or look-alike methods, or immediate `Where`, `Select`, `OrderBy`, `OrderByDescending`, `Skip`, or `Take` composition, which EFD004 covers.

#### Scenario: Stored materialized list
- **WHEN** a materialized list is assigned to a local and reduced in a later statement with an element reducer, a predicate, or an aggregate
- **THEN** EFD019 does not report because the buffered rows may be reused

#### Scenario: Explicit client boundary
- **WHEN** a query crosses `AsEnumerable()` before `First()`
- **THEN** EFD019 does not report

#### Scenario: Query-side reduction
- **WHEN** the reducer is applied to the query before materialization, such as `context.Orders.Count()` or `await context.Orders.FirstAsync()`
- **THEN** EFD019 does not report

#### Scenario: Unrelated same-named methods
- **WHEN** application code calls non-LINQ methods named `ToList`, `First`, or `Count`
- **THEN** EFD019 does not report
