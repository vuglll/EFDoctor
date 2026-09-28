# query-chain-local-tracking Specification

## Purpose
Defines when an EF Core query chain continues through a local variable, how the local's value at a read is chosen, what ends the proof, and how include findings stay unique across a local.

## Requirements

### Requirement: Follow a query local whose value is statically determined
The shared EF Core query-chain proof, and the include-chain analysis of EFD006, EFD017, and EFD025, SHALL continue a query chain through a read of a local variable when the local's value at that read is statically determined. The analysis SHALL then proceed as if the local's value expression were written in place of the read.

A local's value at a read is statically determined when all of these hold:
- The local is declared, with an initializer, by a declaration statement that is a direct child of a block, and the local is not a `ref` local.
- Every other write to the local is a simple assignment statement, `local = value;`, that is a direct child of the same block.
- The local is never passed as a `ref`, `out`, or `in` argument. It is never aliased by a `ref` local, never has its address taken, and is never the target of a deconstruction, compound, null-coalescing, increment, or decrement assignment.
- The read is in a later statement of that block, or nested within such a statement, and not inside a lambda, anonymous method, or local function within that statement.

The value at the read SHALL be the value of the latest write in a statement before the statement that contains the read, or the initializer when there is no such write. A read that does not meet all of these conditions SHALL end the proof as before. The analysis SHALL follow at most 8 locals for one chain.

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
- **THEN** the proof does not follow that local at any read

#### Scenario: Read inside a lambda is not followed
- **WHEN** a query local is read inside a lambda or local function declared after the local
- **THEN** the proof does not follow that read

#### Scenario: Local written through a reference is not followed
- **WHEN** a query local is passed as a `ref` or `out` argument, or is the target of a deconstruction or compound assignment
- **THEN** the proof does not follow that local at any read

#### Scenario: Evidence names the followed local
- **WHEN** a rule that prints the proven chain reports a query traced through a local named `query`
- **THEN** the chain in its evidence contains `local 'query'` at the point where the local was followed

### Requirement: Report include findings once across a followed local
EFD006 and EFD025 report at an include invocation. When a query chain passes through a followed local, the includes in the local's value also belong to the chain that ends at that value's own expression. EFD006 SHALL NOT report, from a later chain end, an include that lies in a followed local's value. EFD025 SHALL report such an include from a later chain end only when an include applied after the local makes it redundant, and it is not already redundant among the includes in the local's value.

#### Scenario: Sibling collection includes split across a local
- **WHEN** `var query = context.Blogs.Include(b => b.Posts);` is followed by `query.Include(b => b.Tags).ToList()`, with no split mode
- **THEN** EFD006 reports exactly one finding, at `Include(b => b.Tags)`

#### Scenario: Sibling collection includes before the local
- **WHEN** a local is initialized with two sibling collection includes, and a later statement composes and executes that local
- **THEN** EFD006 reports exactly one finding, at the second include in the initializer

#### Scenario: Duplicate include split across a local
- **WHEN** `var query = context.Blogs.Include(b => b.Posts);` is followed by `query.Include(b => b.Posts).ToList()`
- **THEN** EFD025 reports exactly one finding, at the second `Include(b => b.Posts)`

#### Scenario: Include covered by a longer path after the local
- **WHEN** `var query = context.Blogs.Include(b => b.Posts);` is followed by `query.Include(b => b.Posts).ThenInclude(p => p.Comments).ToList()`
- **THEN** EFD025 reports the include in the initializer exactly once, as covered by the longer path
