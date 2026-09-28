## MODIFIED Requirements

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
