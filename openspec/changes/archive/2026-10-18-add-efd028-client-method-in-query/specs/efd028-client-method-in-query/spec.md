## Purpose

Define local, high-confidence detection of an application-defined method called inside a SQL-translated EF Core query predicate or ordering, which EF Core cannot translate and which throws at runtime.

## ADDED Requirements

### Requirement: Detect a client-only method in a translated predicate or ordering
EFD028 SHALL report an invocation of a method whose declaration is in the source of the analyzed compilation, including local functions, when all of these hold:
- The invocation is inside a lambda that is an argument to a resolved SQL-translated query operator, or inside a lambda nested in that argument. The SQL-translated query operators are:
  - the `System.Linq.Queryable` operators `Where`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `Any`, `All`, `Count`, `LongCount`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, and `LastOrDefault`;
  - the EF Core async counterparts of the predicate terminals.
- The operator's source is proven to originate at an EF Core `DbSet<T>` or `DbContext.Set<TEntity>()`, as defined by `query-chain-local-tracking`, including through a self-composed query local.
- The invocation's receiver or one of its arguments reads a parameter of a lambda that is the operator's argument or is nested within it.

EFD028 SHALL match operators and methods by resolved symbols, not by name text, and SHALL support reduced extension syntax and the equivalent static call. It SHALL anchor the finding on the offending invocation, and report each invocation once.

#### Scenario: Static helper in a Where predicate
- **WHEN** a proven EF query filters with `Where(o => IsOverdue(o))`, where `IsOverdue` is a static method declared in the project
- **THEN** EFD028 reports the `IsOverdue(o)` invocation

#### Scenario: Instance method on the entity
- **WHEN** a proven EF query filters with `Where(c => c.HasEmail())`, where `HasEmail` is an instance method declared on the entity type
- **THEN** EFD028 reports the `c.HasEmail()` invocation

#### Scenario: Extension method over a column
- **WHEN** a proven EF query filters with `Where(c => c.Name.Normalize2() == value)`, where `Normalize2` is an extension method declared in the project
- **THEN** EFD028 reports the `Normalize2` invocation

#### Scenario: Ordering key
- **WHEN** a proven EF query orders with `OrderBy(o => Priority(o.Status))` or `ThenBy(...)` using a project method that reads the lambda parameter
- **THEN** EFD028 reports the invocation

#### Scenario: Predicate terminal and async terminal
- **WHEN** a proven EF query calls `Any(o => IsOverdue(o))`, `FirstOrDefault(...)`, or `CountAsync(...)` with a project method that reads the lambda parameter
- **THEN** EFD028 reports the invocation

#### Scenario: Call nested in an inner lambda
- **WHEN** a proven EF query filters with `Where(o => o.Lines.Any(l => IsDiscounted(l)))`
- **THEN** EFD028 reports the `IsDiscounted(l)` invocation

#### Scenario: Query through a conditionally composed local
- **WHEN** a self-composed query local is filtered in a branch with `query = query.Where(o => IsOverdue(o));`
- **THEN** EFD028 reports the invocation

#### Scenario: Call that reads no lambda parameter
- **WHEN** a proven EF query filters with `Where(o => o.DueUtc < Cutoff(now))`, where the call reads only captured values or constants
- **THEN** EFD028 does not report, because EF Core evaluates the call on the client as a query parameter

#### Scenario: In-memory or unproven source
- **WHEN** the same predicate is applied to an `IEnumerable<T>`, an in-memory `AsQueryable()` source, an arbitrary `IQueryable<T>` parameter, or a query after `AsEnumerable()`
- **THEN** EFD028 does not report

#### Scenario: Projection is not reported
- **WHEN** a project method that reads the lambda parameter is called inside `Select`
- **THEN** EFD028 does not report in the first version

#### Scenario: Framework and EF methods are not reported
- **WHEN** a predicate calls methods declared outside the analyzed source, such as `string.IsNullOrEmpty`, `EF.Functions.Like`, or a method from a referenced assembly
- **THEN** EFD028 does not report

### Requirement: Exclude methods that EF Core or a query-rewriting extension can translate
EFD028 SHALL NOT report an invocation whose target method meets any of these conditions:
- It is marked with an attribute named `DbFunctionAttribute`, `ProjectableAttribute`, `InjectLambdaAttribute`, `ComputedAttribute`, or `ExpandableAttribute`.
- Its name appears as an identifier or string literal in the arguments of a `HasDbFunction` invocation in the same compilation.
- It is a static method declared on a type derived from `DbContext`.
- Its body consists only of a `throw`.

#### Scenario: DbFunction attribute
- **WHEN** a predicate calls a method marked `[DbFunction]`
- **THEN** EFD028 does not report

#### Scenario: HasDbFunction registration
- **WHEN** a predicate calls a method that the model registers with `modelBuilder.HasDbFunction(typeof(Functions).GetMethod(nameof(Functions.Distance)))`
- **THEN** EFD028 does not report

#### Scenario: Database-function stub
- **WHEN** a predicate calls a static method declared on the `DbContext`, or a method whose body is only `throw new NotSupportedException()`
- **THEN** EFD028 does not report

#### Scenario: Query-rewriting attribute
- **WHEN** a predicate calls a method marked `[Projectable]`, `[InjectLambda]`, `[Computed]`, or `[Expandable]`
- **THEN** EFD028 does not report

### Requirement: Produce actionable EFD028 findings
Each EFD028 finding SHALL have:
- rule title `Client-only method in a SQL-translated query`;
- `Correctness` category, `Warning` severity, and high confidence;
- documentation key `EFD028`;
- the message `Method '{method}' cannot be translated to SQL inside this query's {operator}`.

Evidence SHALL name the resolved method, the operator, and the proven EF origin. Impact SHALL explain that EF Core cannot translate the method and throws when the query executes. Remediation SHALL recommend three fixes:
- inline the logic as a translatable expression;
- map the method as a database function;
- materialize before filtering, only when the result is known to be small.

Remediation SHALL also name a library-rewritten method as a legitimate suppression.

#### Scenario: Complete structured finding
- **WHEN** EFD028 reports an invocation
- **THEN** the finding has the title, severity, confidence, documentation key, evidence, impact, and remediation described above, and spans the invocation

### Requirement: Support standard suppression and existing reporting behavior
EFD028 SHALL use standard Roslyn suppression (pragma, editor configuration, and `SuppressMessage("Correctness", "EFD028")`). It SHALL flow through the existing CLI, console and JSON output, deterministic ordering, and exit codes, without a new command or schema version.

#### Scenario: Pragma and editor configuration suppression
- **WHEN** a matching invocation is covered by `#pragma warning disable EFD028`, or editor configuration sets `dotnet_diagnostic.EFD028.severity = none`
- **THEN** the finding is excluded

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan of the EFD028 fixture project runs in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD028 findings, with exact locations, JSON schema version `1`, and exit code `1`
