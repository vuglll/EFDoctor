# Design

## Context

EFD027 already proves that two operations run on the same `DbContext` instance: the context is a never-written local or parameter, a `this` or static field or auto-property, or `this`, compared by symbol. That logic is private to its analyzer. EFD013 recognizes the four bulk methods by name on EF Core's extension types. `EfQueryOperationAnalysis.TryAnalyzeSource` proves a query chain and lists its operators, including `AsNoTracking`.

In EF Core 7 and 8 the bulk methods are declared on `RelationalQueryableExtensions`; from EF Core 9 they are on `EntityFrameworkQueryableExtensions`.

## Goals / Non-Goals

**Goals:**

- Report only when the load, the bulk operation, and the context are all visible in one method, in an order that is certain.
- Separate "left stale" from "stale and used", because only the second is observable in the method.

**Non-Goals:**

- Context scope across methods, such as a load in one repository method and a bulk operation in another.
- Deciding whether the bulk operation's filter can match the loaded rows.
- Loads that don't go into a local, tracked entities that arrive through `Add` or `Attach`, and navigation fix-up.

## Decisions

### Share the context-identity proof

The symbol-based context key and the "never written" check move from EFD027 into an internal `EfContextIdentity`, constructed per operation block. EFD027 calls it and keeps its behavior; its tests cover the move.

### A tracked load is a local declaration

A tracked load is a local declaration statement whose initializer, after any `await`, is one of:

- `DbSet<T>.Find` or `FindAsync`;
- `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `ToList`, `ToArray`, or their EF Core async forms, on a proven query whose element type is the entity type and whose chain has no `AsNoTracking` or `AsNoTrackingWithIdentityResolution`.

Requiring a local gives the finding something to name, and lets the rule see later uses. A context configured with `QueryTrackingBehavior.NoTracking` can't be seen, which is one reason the base confidence is medium.

### Order is proven by block structure

The load must be a statement directly inside a block, and the bulk operation must sit in a later statement of that same block, at any depth, but not inside a lambda or local function. Then every path that reaches the bulk operation has run the load. A load and a bulk operation in sibling branches never match.

A loop can run a later load before an earlier bulk operation on its next iteration. That is not followed.

### The same entity type on the same context

The bulk operation's query is proven to a `DbSet<T>`, and `T` must equal the loaded entity type. The two context keys must be equal. Derived and base types are not matched in this version.

### Silence on clear, reload, and detach

A load is ignored when any later statement of its block contains, on the same context, `ChangeTracker.Clear()`, `Entry(…).Reload()` or `ReloadAsync()`, or `Entry(…).State = EntityState.Detached`. This is deliberately coarse: any of them shows that the author is managing tracker state, and a rule that second-guesses which entity was reloaded would be noisy.

### Two tiers

After the bulk operation, in the remaining statements of the load's block, the rule looks for:

- a reference to the loaded local;
- another tracked load of the same type from the same context, which returns the tracked instance;
- `SaveChanges` or `SaveChangesAsync` on the same context.

Any of them makes the finding high confidence, and the evidence names it. Otherwise the confidence is medium: the entities are stale, but this method doesn't use them. Decided with the maintainer.

The descriptor is `Warning` under `Correctness`. In a build, the shared diagnostic factory makes the medium findings suggestions.

### One finding per bulk operation

The finding is on the bulk operation's invocation. When several loads qualify, the closest one before the bulk operation is named.

## Risks / Trade-offs

- [The bulk filter can't match the loaded rows, for example a cleanup of expired rows after loading active ones] → The rule can't compare predicates. The rule page names this as the case to suppress, and the evidence shows both expressions.
- [A no-tracking default on the context] → Invisible to static analysis; documented.
- [The coarse silence rules hide a real finding] → Accepted in favor of precision.

## Migration Plan

None. EFD038 is a new rule.
