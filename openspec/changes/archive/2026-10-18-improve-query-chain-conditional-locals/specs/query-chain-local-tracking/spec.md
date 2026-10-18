## MODIFIED Requirements

### Requirement: Follow a query local whose value is statically determined
The shared EF Core query-chain proof, and the include-chain analysis of EFD006, EFD017, and EFD025, SHALL continue a query chain through a read of a local variable when the local's value at that read is statically determined. The analysis SHALL then proceed as if the local's value expression were written in place of the read.

A local's value at a read is statically determined when all of these hold:
- The local is declared, with an initializer, by a declaration statement that is a direct child of a block, and the local is not a `ref` local.
- Every other write to the local is a simple assignment statement, `local = value;`, that is a direct child of the same block.
- The local is never passed as a `ref`, `out`, or `in` argument. It is never aliased by a `ref` local, never has its address taken, and is never the target of a deconstruction, compound, null-coalescing, increment, or decrement assignment.
- The read is in a later statement of that block, or nested within such a statement, and not inside a lambda, anonymous method, or local function within that statement.

The value at the read SHALL be the value of the latest write in a statement before the statement that contains the read, or the initializer when there is no such write. A read that does not meet all of these conditions SHALL end this proof as before. The analysis SHALL follow at most 8 locals for one chain.

#### Scenario: Query stored in a local and then executed
- **WHEN** a method declares `var query = context.Items.Where(i => i.Active);` and a later statement calls `query.ToList()`
- **THEN** the proof traces `query.ToList()` to the `DbSet` origin through `query`, with the `Where` composition applied

#### Scenario: Straight-line recomposition
- **WHEN** a local is initialized from an EF query and later statements of the same block reassign it as `query = query.OrderBy(...);` before it is executed
- **THEN** the proof includes every recomposition written before the executing statement, in order

#### Scenario: Chain through several locals
- **WHEN** `var all = context.Items;`, `var active = all.Where(...);`, and a later `active.ToList()` appear in one block
- **THEN** the proof traces the execution to the `DbSet` origin through both locals

#### Scenario: Conditional recomposition is not followed
- **WHEN** a query local is reassigned inside an `if` statement, a loop body, or any block other than the one that declares it
- **THEN** the full chain proof does not follow that local at any read, and only the origin proof described in "Prove the origin of a self-composed query local" may follow it

#### Scenario: Read inside a lambda is not followed
- **WHEN** a query local is read inside a lambda or local function declared after the local
- **THEN** the proof does not follow that read

#### Scenario: Local written through a reference is not followed
- **WHEN** a query local is passed as a `ref` or `out` argument, or is the target of a deconstruction or compound assignment
- **THEN** the proof does not follow that local at any read

#### Scenario: Evidence names the followed local
- **WHEN** a rule that prints the proven chain reports a query traced through a local named `query`
- **THEN** the chain in its evidence contains `local 'query'` at the point where the local was followed

## ADDED Requirements

### Requirement: Prove the origin of a self-composed query local
EFD004, EFD009, EFD010, EFD013, EFD019, EFD022, and EFD023 SHALL use an origin proof. It follows a read of a query local that the full chain proof can't follow, when the local is self-composed. No other rule SHALL use it.

A local is self-composed when all of these hold:
- The local is declared with an initializer and is not a `ref` local.
- The initializer is proven to originate at a `DbSet<T>` property or `DbContext.Set<T>()`, through the chain proof, including other followed locals.
- Every other write to the local is a simple assignment statement, `local = value;`, anywhere in the method, including inside branches, loops, lambdas, and local functions.
- Every such value composes a read of the same local through resolved `Queryable` operators that keep the element type and through EF Core's bound-neutral composition methods, and through nothing else. The `Queryable` operators are `Where`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `Skip`, `Take`, and `Distinct`. A conditional expression whose arms each meet this condition, or are a read of the same local, also qualifies.
- The local meets the reference conditions of the full chain proof: it is never written through `ref`, `out`, or `in`, never aliased, never has its address taken, and is never the target of a deconstruction, compound, null-coalescing, increment, or decrement assignment.

When the origin proof follows a read, it SHALL trace the read to the initializer's origin. The proven operations SHALL be the initializer's operations followed by `local '<name>' (conditionally composed)`. They SHALL contain none of the operations written in the later writes. The full chain proof SHALL be tried first, and the origin proof SHALL apply only when the full chain proof can't follow the read.

#### Scenario: Conditionally composed query is traced to its origin
- **WHEN** `var query = context.Items.AsNoTracking();` is followed by `if (name != null) query = query.Where(i => i.Name == name);` and a later `query.ToList()`
- **THEN** an origin-proof rule traces `query.ToList()` to the `DbSet` origin `context.Items`, and the proven operations are `EF.AsNoTracking -> local 'query' (conditionally composed)`

#### Scenario: Read inside the conditional branch
- **WHEN** a self-composed local is read inside the branch that reassigns it, as in `if (x) query = query.Where(p => p.Name.ToLower() == s);`
- **THEN** an origin-proof rule traces that read to the initializer's origin

#### Scenario: Composition in a loop or a conditional expression
- **WHEN** a query local is reassigned as `query = query.Where(...)` inside a loop body, or as `query = flag ? query.OrderBy(...) : query;`
- **THEN** the local is self-composed, and an origin-proof rule traces its reads to the initializer's origin

#### Scenario: Replacement from another query is not followed
- **WHEN** a branch reassigns a query local from a different query, as in `if (x) query = context.Archived.Where(...);`, or through an operator that changes the element type or combines queries, such as `Select`, `Concat`, or `Union`
- **THEN** the origin proof does not follow that local at any read

#### Scenario: Local written through a reference is not followed by the origin proof
- **WHEN** a query local is passed as a `ref` or `out` argument, assigned inside an expression rather than as a statement, or is the target of a deconstruction or compound assignment
- **THEN** the origin proof does not follow that local at any read

#### Scenario: Rules that report an absence do not use the origin proof
- **WHEN** a self-composed query local is executed, and a conditional branch adds a `Take`, an ordering, an `Include`, or a filter
- **THEN** EFD002, EFD005, EFD006, EFD014, EFD017, EFD020, EFD025, EFD027, EFD037, and EFD038 do not follow that local, and report nothing they wouldn't report for an unknown `IQueryable<T>`

#### Scenario: Conditional operations are not in the proven chain
- **WHEN** a self-composed query local is ordered only inside a conditional branch and then materialized and reduced with `Last()`
- **THEN** EFD019 does not report, because the proven operations contain no `OrderBy`

### Requirement: Treat DbSet AsQueryable as the set
The shared EF Core query-chain proof SHALL treat a call to EF Core's `DbSet<T>.AsQueryable()` instance method as the `DbSet<T>` it is called on. A chain that starts with `context.Items.AsQueryable()` SHALL be traced to the origin `context.Items`, and the proven operations SHALL begin with `DbSet.AsQueryable`.

#### Scenario: Query started with DbSet AsQueryable
- **WHEN** a method calls `context.Items.AsQueryable().Where(i => i.Active).ToList()`, or stores `context.Items.AsQueryable()` in a query local
- **THEN** the proof traces the query to the `DbSet` origin `context.Items`, as it would for `context.Items.Where(i => i.Active).ToList()`

