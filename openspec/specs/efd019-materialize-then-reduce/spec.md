# EFD019 Materialize-Then-Reduce Specification

## Purpose

Defines high-confidence detection and reporting for EF Core queries that are fully materialized and then immediately reduced on the client to a single element, count, existence check, or aggregate that the database could compute directly.

## Requirements

### Requirement: Detect materialize-then-reduce queries
EFD019 SHALL report a high-confidence finding when a semantically resolved `ToList` or `ToArray` materializer, or an awaited EF Core `ToListAsync` or `ToArrayAsync` materializer, whose inline source is proven to originate from an EF Core `DbSet<T>` or `DbContext.Set<T>()`, is immediately consumed through parentheses or implicit conversions by a supported reducer.

#### Scenario: Element reduction after ToList
- **WHEN** a proven EF query is materialized with `ToList()` and the result is immediately reduced with `First()`
- **THEN** EFD019 reports that the whole result is buffered only to keep one element

#### Scenario: Count after ToArray
- **WHEN** a proven EF query is materialized with `ToArray()` and the result is immediately reduced with `Count()` or its `Length` property
- **THEN** EFD019 reports that the whole result is buffered only to count it

#### Scenario: Awaited asynchronous materializer
- **WHEN** a proven EF query is materialized with an awaited `ToListAsync()` and the awaited list is immediately reduced with `Any()` or its `Count` property
- **THEN** EFD019 reports the materialize-then-reduce shape

#### Scenario: Query composition before materialization
- **WHEN** resolved `Queryable` or EF Core composition such as `Where`, `OrderBy`, `Include`, or `AsNoTracking` precedes an eligible materializer
- **THEN** EFD019 still evaluates the immediate reducer

### Requirement: Classify supported reducers conservatively
EFD019 SHALL support `Enumerable.First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Any`, `All`, `Count`, `LongCount`, `Sum`, `Min`, `Max`, and `Average`, together with the `List<T>.Count` and array `Length` properties. It SHALL support `Last` and `LastOrDefault` only when the inline query chain contains a resolved `Queryable.OrderBy` or `OrderByDescending`. A predicate argument SHALL be accepted only when it is a single-parameter lambda built from entity property paths, constants, captured values, null checks, built-in comparisons, and boolean combinations. A selector argument SHALL be accepted only when it is a single-parameter lambda returning a direct entity property path. Parameterless `Sum`, `Min`, `Max`, and `Average` SHALL be accepted only when the materialized element type is a value type or `string`.

#### Scenario: Predicate reducer with supported lambda
- **WHEN** a materialized result is immediately reduced with `FirstOrDefault(order => order.Id == id)` or `Any(order => order.Active)`
- **THEN** EFD019 reports the finding

#### Scenario: Aggregate over projected scalars
- **WHEN** a query projects a numeric property, is materialized, and is immediately reduced with `Sum()` or `Max()`
- **THEN** EFD019 reports the finding

#### Scenario: Aggregate with property selector
- **WHEN** a materialized entity result is immediately reduced with `Sum(order => order.Total)`
- **THEN** EFD019 reports the finding

#### Scenario: Unsupported lambda or overload
- **WHEN** a reducer uses a lambda containing method calls, computed members, or captured delegates, uses an indexed or comparer overload, or uses a `FirstOrDefault(defaultValue)` overload
- **THEN** EFD019 does not report

#### Scenario: Unordered Last
- **WHEN** a materialized result without a query-side `OrderBy` or `OrderByDescending` is reduced with `Last()` or `LastOrDefault()`
- **THEN** EFD019 does not report because EF Core cannot translate an unordered `Last`

### Requirement: Preserve boundaries and existing rules
EFD019 SHALL identify materializers, reducers, and query origins by resolved symbols. It SHALL NOT report explicit client-evaluation boundaries such as `AsEnumerable` or `AsAsyncEnumerable`, materialized values stored in locals, fields, properties, or parameters before reduction, arbitrary `IQueryable<T>` values or in-memory queryables, unresolved or look-alike methods, or immediate `Where`, `Select`, `OrderBy`, `OrderByDescending`, `Skip`, or `Take` composition, which EFD004 covers.

#### Scenario: Stored materialized list
- **WHEN** a materialized list is assigned to a local and reduced in a later statement
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

### Requirement: Produce actionable and qualified findings
Each EFD019 finding SHALL use warning severity and high confidence, provide precise source coordinates covering the complete materializer invocation, name the resolved materializer, proven query origin, and reducer as evidence, explain that every row is transferred, buffered, and possibly tracked only to be reduced on the client, and recommend applying the reducer to the query or using its asynchronous EF Core counterpart. The finding SHALL note that keeping the materialized list is appropriate only when its rows are also needed elsewhere.

#### Scenario: Finding contract
- **WHEN** EFD019 reports a materialize-then-reduce query
- **THEN** the finding contains non-empty evidence, qualified impact, practical remediation naming the query-side or asynchronous reducer, precise materializer coordinates, and documentation key `EFD019`

### Requirement: Support standard suppression and generated-code exclusion
EFD019 SHALL honor standard Roslyn suppression and SHALL exclude generated code consistently with the other source analyzers.

#### Scenario: Suppressed finding
- **WHEN** an otherwise matching expression is suppressed by pragma, analyzer configuration, or `SuppressMessage`
- **THEN** EFD019 emits no finding for that expression

#### Scenario: Generated source
- **WHEN** an otherwise matching expression appears in generated source
- **THEN** EFD019 emits no finding

### Requirement: Validate precision with representative fixtures
EFD019 SHALL have at least ten positive and ten negative analyzer fixtures covering synchronous and awaited asynchronous materializers, element, existence, count, property-count, and aggregate reducers, supported and unsupported lambdas, ordered and unordered `Last`, static and reduced invocation forms, explicit client boundaries, stored lists, in-memory and arbitrary queryables, EFD004-covered operators, unrelated methods, generated code, exact locations, diagnostic properties, suppression, and the EFD005 yield.

#### Scenario: Curated validation suite
- **WHEN** the EFD019 analyzer fixture suite runs
- **THEN** every curated materialize-then-reduce case reports and every curated query-side, bounded, stored, or unrelated case remains clean
