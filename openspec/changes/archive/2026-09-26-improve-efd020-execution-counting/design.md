# Design

## Context

`MultipleEnumerationAnalyzer` does its work in two passes over each operation block:

1. It collects candidate locals: `IQueryable` initializers traced to a `DbSet`.
2. It increments a per-local counter for every `foreach` over the local and every terminal invocation whose source is the local.

The counter doesn't know where a use sits, or which overload a terminal uses.

## Goals / Non-Goals

**Goals:**
- Count only real, separate executions of the same query.

**Non-Goals:**
- Loops. An execution inside a loop still counts once, as it does today.
- Data flow across calls or through reassignments. The reassignment exclusion is unchanged.

## Decisions

### Expression-tree uses

**Decision:** Before counting a use, walk its ancestors. If it is inside an `IAnonymousFunctionOperation` whose converted type is a `System.Linq.Expressions.Expression<TDelegate>`, skip it. The converted type is the `Type` of the parent `IConversionOperation` or `IDelegateCreationOperation`. The check applies to every enclosing lambda, not just the innermost one. A delegate lambda nested inside an expression tree, such as `e => e.Items.Any(i => ids.Contains(i.Id))`, is still part of that tree.

**Why:** In practice, `Queryable` composition always receives expression trees. EF composes a query local used inside them as a subquery. Delegate lambdas, such as `list.Select(x => q.Count())`, execute on the client and must still count.

### Predicate overloads

**Decision:** Skip a terminal invocation when any argument's parameter is named `predicate`. That covers `Queryable` and `EntityFrameworkQueryableExtensions` predicate overloads such as `Any`, `All`, `Count`, `LongCount`, `First*`, `Single*`, and `Last*`.

**Why:** Parameter names are part of the public API of both classes and are stable. Selector parameters are named `selector`, `keySelector`, and so on, so they keep counting.

**Alternative:** Excluding locals initialized to a bare `DbSet`. Rejected, because every corpus case is already covered by the predicate rule, and `var q = context.Entities; q.ToList(); q.ToList();` is a real double load that EFD020 already tests.

### Counting across conditionals

**Decision:**

1. For each local, collect the list of counted execution operations.
2. Compute the maximum number of them on one path, with a recursive split:
   1. Find a conditional operation (`IConditionalOperation`, which covers both `?:` and `if`) that has executions in both `WhenTrue` and `WhenFalse`. Pick one with none of those executions nested in another such conditional's arm; that is, choose the outermost one.
   2. Split the executions into three groups: `O` (outside it), `T` (in `WhenTrue`), and `F` (in `WhenFalse`).
   3. The result is `max(count(O ∪ T), count(O ∪ F))`.
   4. If no conditional has executions in both arms, the count is the size of the set.
3. Report when the result is at least 2.

The evidence keeps reporting this count.

**Why:** This handles nested and sequential conditionals correctly, and the sets are tiny.

## Risks / Trade-offs

- **[Risk]** A use inside an expression tree that EF cannot translate, and therefore evaluates on the client, is no longer counted. → That shape fails at runtime in modern EF Core, which rejects client evaluation of untranslatable subqueries. Missing it has a low cost.
- **[Risk]** `q.First(x => x.Id == 1)` followed by `q.First(x => x.Id == 1)`, which is the same predicate twice, is no longer reported. → This is rare, and it is accepted as a false negative.
