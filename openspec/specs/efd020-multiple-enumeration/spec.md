# EFD020 Multiple Enumeration Specification

## Purpose

Define local, medium-confidence detection of an EF Core `IQueryable` local that is enumerated or re-executed more than once in the same method, causing repeated database round trips.

## Requirements

### Requirement: Detect multiple enumeration of an IQueryable local
EFD020 SHALL report a local variable whose declared type is, or implements, `System.Linq.IQueryable<T>` and whose initializer traces through supported query composition to an EF Core `DbSet` or `DbContext.Set<TEntity>()`, when that local is executed two or more times within the same method body. An execution is a `foreach` over the local, or an invocation of a materializing or aggregating LINQ terminal operator (for example `ToList`, `ToArray`, `Count`, `Any`, `First`, `Single`, `Sum`, and their `Async` variants) whose source is the local.

The following SHALL NOT be counted as executions:
- a terminal operator called with a `predicate` argument, such as `q.Any(x => …)` or `q.FirstOrDefault(x => …)`, because it runs a different query over the same base. A terminal with a selector argument, such as `q.Sum(x => x.Total)` or `q.ToDictionary(x => x.Id)`, is still an execution;
- a use inside a lambda that is converted to an expression tree (`System.Linq.Expressions.Expression<TDelegate>`), such as `other.Where(x => q.Contains(x.Id))`, because the query provider composes it into the enclosing query. A use inside a lambda converted to an ordinary delegate is still an execution.

Executions in the two arms of the same conditional (a conditional expression or an `if`/`else` statement) are mutually exclusive. The count SHALL be the largest number of executions that can occur on one path through the method. Matching SHALL use resolved method and type symbols rather than name text. EFD020 SHALL anchor the finding on the local's declaration. EFD020 SHALL NOT report a local that is reassigned after its declaration, nor one executed at most once.

#### Scenario: Query executed twice
- **WHEN** a local `q` is initialized from a proven EF query and the method calls `q.Any()` and later `q.ToList()`
- **THEN** EFD020 reports the declaration of `q`

#### Scenario: Foreach plus terminal
- **WHEN** a local `q` from a proven EF query is both `foreach`-enumerated and passed to a terminal operator such as `q.Count()`
- **THEN** EFD020 reports the declaration of `q`

#### Scenario: Executed once is not reported
- **WHEN** a local `q` from a proven EF query is executed exactly once (for example a single `ToList`)
- **THEN** EFD020 does not report

#### Scenario: Materialized once and reused is not reported
- **WHEN** the local is a materialized result (for example `var list = query.ToList();`) enumerated multiple times
- **THEN** EFD020 does not report, because the list is not a deferred query

#### Scenario: Reassigned local is not reported
- **WHEN** the local is assigned a different value after its declaration
- **THEN** EFD020 does not report, to avoid conflating distinct queries

#### Scenario: Arbitrary or in-memory source
- **WHEN** the local's initializer is an arbitrary `IQueryable<T>` not proven to originate from a `DbSet`, or an in-memory sequence
- **THEN** EFD020 does not report

#### Scenario: Use inside an expression-tree lambda is not an execution
- **WHEN** a local `ids` from a proven EF query is used only as `ids.Contains(x.Id)` or `ids.Any(...)` inside the `Where` lambdas of other queries
- **THEN** EFD020 does not report, because each use is composed into the enclosing query's SQL

#### Scenario: Use inside a delegate lambda is an execution
- **WHEN** a local `q` from a proven EF query is used as `q.Count()` inside an ordinary delegate lambda, and is also executed elsewhere in the method
- **THEN** EFD020 counts both executions and reports

#### Scenario: Predicate terminals are distinct queries
- **WHEN** a local `q` from a proven EF query is only used through predicate terminals such as `q.Any(x => x.Name == "A")` and `q.FirstOrDefault(x => x.Name == "B")`
- **THEN** EFD020 does not report

#### Scenario: Selector terminals still count
- **WHEN** a local `q` from a proven EF query is executed as `q.Sum(x => x.Total)` and `q.ToList()`
- **THEN** EFD020 reports, because both execute the same query

#### Scenario: Mutually exclusive branches are not added together
- **WHEN** a local `q` is executed once in each arm of `flag ? await q.ToListAsync() : q.ToList()`, or once in each branch of an `if`/`else`
- **THEN** EFD020 does not report, because only one execution runs

#### Scenario: Execution before a branch still counts
- **WHEN** a local `q` is executed with `q.Any()` and then once in either branch of an `if`/`else`
- **THEN** EFD020 reports two executions on each path

### Requirement: Produce actionable and qualified EFD020 findings
Each EFD020 finding SHALL have rule title `Query is enumerated more than once`, medium confidence, and documentation key `EFD020`, populate the shared finding contract, and span the local declaration using one-based coordinates. Evidence SHALL identify the local, its proven EF origin, and the number of executions observed. Impact language SHALL explain that each enumeration re-executes the query and issues another database round trip, without claiming a specific runtime cost. Remediation SHALL recommend materializing the query once (for example into a `List<T>` with `ToList`/`ToListAsync`) and reusing the result, and SHALL acknowledge that an intentional re-query (for example to observe changed data) can be suppressed with a recorded reason.

#### Scenario: Complete structured finding
- **WHEN** EFD020 reports a query
- **THEN** console and JSON output contain every shared finding field with documentation key `EFD020`

#### Scenario: Exact diagnostic location
- **WHEN** EFD020 reports a local
- **THEN** the finding range identifies the local declaration using one-based coordinates

#### Scenario: Qualified remediation
- **WHEN** EFD020 reports a finding
- **THEN** remediation recommends materializing once and reusing, and names an intentional re-query as a legitimate suppressible case

### Requirement: Support standard suppression and existing reporting behavior
EFD020 SHALL use standard Roslyn diagnostic suppression (pragma, editor configuration, and `SuppressMessage` where supported) and flow through the existing local-only CLI, deterministic ordering, de-duplication, and stable exit codes without a new command, schema version, network activity, or telemetry.

#### Scenario: Pragma suppression
- **WHEN** a matching local declaration is covered by `#pragma warning disable EFD020`
- **THEN** the analyzer result consumed by the CLI excludes that finding until warning restoration

#### Scenario: Editor configuration suppression
- **WHEN** applicable editor configuration sets `dotnet_diagnostic.EFD020.severity = none`
- **THEN** the analyzer result consumed by the CLI excludes EFD020 for that scope

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan produces EFD020 findings
- **THEN** both output formats contain the complete finding in deterministic order and retain the successful-scan-with-findings exit code and JSON schema version `1`

### Requirement: Validate EFD020 with focused and end-to-end tests
The change SHALL include analyzer fixtures covering positive cases (two terminals, `foreach` plus terminal, `Any` guard then enumerate) and negative cases (single execution, materialized-then-reused list, reassigned local, arbitrary `IQueryable`, in-memory source, and standard suppression), and SHALL include at least one CLI end-to-end test that verifies console and JSON output for an EFD020 fixture project.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** positive and negative EFD020 fixtures verify detection, the single-execution and materialization exclusions, reassignment exclusion, source exclusions, and suppression

#### Scenario: CLI end-to-end fixture
- **WHEN** the CLI analyzes the EFD020 fixture project in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD020 findings with exact locations and JSON schema version `1`
