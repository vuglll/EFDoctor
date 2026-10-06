# Spec Delta

## Purpose

Defines advisory detection and reporting of EF Core queries that materialize full entities into a local when the method reads only a small subset of the entity's mapped scalar properties.

## ADDED Requirements

### Requirement: Detect materialized entities of which only scalar properties are read
EFD037 SHALL analyze a semantically resolved `Enumerable.ToList`, `Enumerable.ToArray`, or awaited EF Core `ToListAsync` or `ToArrayAsync` whose source is proven to originate from an EF Core `DbSet<TEntity>` or `DbContext.Set<TEntity>()`, whose element type is `TEntity`, and whose value initializes a local variable. It SHALL report the materializer when every reference to that local in the containing method is a supported use, every reference to an element is the instance of a read of a counted property, and the properties read meet the threshold.

The supported uses of the local are:

- the collection of a `foreach` statement with a single loop variable;
- the source of `Enumerable.Select` with a one-parameter selector lambda;
- the source of `Enumerable.Any`, `All`, `Count`, `Sum`, `Min`, `Max`, or `Average` with a one-parameter lambda;
- the source of `Enumerable.Any` or `Enumerable.Count` with no lambda, or the instance of `List<T>.Count` or an array's `Length`;
- an index read that is itself the instance of a property read.

An element is a `foreach` loop variable, a lambda parameter, or an indexed value of the local.

#### Scenario: Result projected in memory
- **WHEN** `var orders = await db.Orders.Where(o => o.CustomerId == id).ToListAsync();` is followed only by `orders.Select(o => new OrderSummary(o.Id, o.Total))`, and `Order` has eight counted properties
- **THEN** EFD037 reports the `ToListAsync` invocation

#### Scenario: Result read in a foreach loop
- **WHEN** the local is used only as the collection of a `foreach` whose body reads two counted properties of the loop variable
- **THEN** EFD037 reports the materializer

#### Scenario: Synchronous and array materializers
- **WHEN** the local is initialized by `ToList()`, `ToArray()`, or an awaited `ToArrayAsync()`
- **THEN** EFD037 reports each the same way

#### Scenario: Lambda reducers
- **WHEN** the local is used only by `Any`, `All`, `Count`, `Sum`, `Min`, `Max`, or `Average` with a lambda that reads counted properties
- **THEN** EFD037 reports the materializer

#### Scenario: Element read by index
- **WHEN** the local is used only as `orders[0].Total`
- **THEN** EFD037 reports the materializer

#### Scenario: Several uses combined
- **WHEN** the local is read by a `foreach`, by `Select`, and by `Count`, and the properties read across all uses meet the threshold
- **THEN** EFD037 reports once, and the evidence names every property read

#### Scenario: Query composed through a local
- **WHEN** the query is held in a statically determined `IQueryable` local before it is materialized into the result local
- **THEN** EFD037 reports the materializer

### Requirement: Count mapped scalar properties and apply a threshold
The counted properties of an entity SHALL be its public instance auto-properties that have a getter and a setter, are declared on the entity type or a base type, carry no `NotMapped` attribute, and have a scalar type: a primitive, `string`, `decimal`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly`, `Guid`, an enum, `byte[]`, or a nullable form of one. EFD037 SHALL report only when the method reads at least one counted property, at most half of them, and leaves at least four unread.

#### Scenario: Threshold met
- **WHEN** the method reads two of an entity's eight counted properties
- **THEN** EFD037 reports

#### Scenario: More than half read
- **WHEN** the method reads five of an entity's eight counted properties
- **THEN** EFD037 does not report

#### Scenario: Fewer than four unread
- **WHEN** the method reads one of an entity's four counted properties
- **THEN** EFD037 does not report

#### Scenario: No property read
- **WHEN** the method only counts the local's elements
- **THEN** EFD037 does not report

#### Scenario: Inherited properties are counted
- **WHEN** the entity inherits counted properties from a base type
- **THEN** they count toward the entity's total, and reading one counts as a read

#### Scenario: Navigations and unmapped members are not counted
- **WHEN** the entity has navigation, collection, `NotMapped`, computed, or get-only properties
- **THEN** they do not count toward the entity's total

### Requirement: Stay silent when an entity may be needed whole
EFD037 MUST NOT report when any reference to the local or to an element is outside the supported uses, including when an entity or the local is returned, passed as an argument, assigned or stored, captured into another collection, compared, or has a member written; when a navigation, collection, uncounted property, method, or field of an element is used; when the local is reassigned; when the query projects with `Select` or loads navigations with `Include` or `ThenInclude`; or when the source is not a proven EF Core query.

#### Scenario: Entity returned or passed on
- **WHEN** the local or an element is returned from the method or passed to another method
- **THEN** EFD037 does not report

#### Scenario: Entity stored
- **WHEN** the local or an element is assigned to a field, a property, another local, or added to a collection
- **THEN** EFD037 does not report

#### Scenario: Entity modified
- **WHEN** a property of an element is assigned, incremented, or passed by reference
- **THEN** EFD037 does not report

#### Scenario: Navigation or uncounted member used
- **WHEN** the method reads a navigation, a collection, an uncounted property, or calls a method on an element
- **THEN** EFD037 does not report

#### Scenario: Unsupported use of the local
- **WHEN** the local is used by an operator that returns entities, such as `Where`, `OrderBy`, or `First`, or is reassigned
- **THEN** EFD037 does not report

#### Scenario: Query already shaped
- **WHEN** the query projects with `Select` before it is materialized, or uses `Include` or `ThenInclude`
- **THEN** EFD037 does not report

#### Scenario: Unproven source
- **WHEN** the materialized collection does not originate from a proven EF Core query, or EF Core is not referenced
- **THEN** EFD037 does not report

### Requirement: Report advisory, evidence-based findings
Each EFD037 finding SHALL use `Info` severity, advisory confidence, and the `Performance` category, and SHALL be located on the complete materializer invocation. The evidence SHALL name the query origin, the result local, the entity type, the properties read, and the number of counted properties. The finding SHALL describe likely impact without claiming a measured cost, SHALL recommend projecting the read properties with `Select` in the query, and SHALL name the cases where loading the entity is right. Standard Roslyn suppression SHALL apply, and generated code SHALL NOT be analyzed.

#### Scenario: Complete structured finding
- **WHEN** EFD037 reports
- **THEN** the diagnostic has `Info` severity, advisory confidence, evidence naming the origin, the local, the entity, the properties read, and the counted total, a qualified impact, a remediation that names `Select`, and the documentation key `EFD037`

#### Scenario: Diagnostic location
- **WHEN** EFD037 reports an awaited `ToListAsync`
- **THEN** the location spans the materializer invocation from the start of the query to the closing parenthesis, without the `await`

#### Scenario: Standard suppression
- **WHEN** the finding is suppressed with a pragma, `SuppressMessage`, or a configured `none` severity
- **THEN** it is not reported

#### Scenario: Generated code
- **WHEN** the pattern occurs in generated code
- **THEN** EFD037 does not report
