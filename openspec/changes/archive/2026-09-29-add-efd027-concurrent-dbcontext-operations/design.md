# Design

## Context

Several building blocks already exist:

- `EfAsyncOperations.TryGetCoreOperation` recognizes the resolved EF Core asynchronous operations used by EFD011 and EFD018.
- `EfQueryOperationAnalysis.TryAnalyzeSource` proves a query's `DbSet`/`Set<T>()` origin, including through statically determined query locals.
- `EfQueryLocals.TryResolve` returns the value a plain block-level local holds at a read.

None of them answers the question EFD027 needs answered: which `DbContext` instance does an operation run on, and is it provably the same instance as another operation's? See `proposal.md` for the motivation and the EFD027 spec delta for the observable behavior.

## Goals / Non-Goals

**Goals:**

- Report only when the same context instance is proven by symbol and the overlap is proven by evaluation order. A timing-dependent runtime failure is exactly what users can't catch in testing, so precision matters more than recall.
- Reuse the existing operation recognition and origin proof.

**Non-Goals:**

- Synchronous EF operations as the second operation, for example `db.Orders.ToList()` while a task is pending. That is a real bug too, but it needs the sync-operation set from EFD010. It is a candidate follow-up.
- `Parallel.For`/`ForEach`/`ForEachAsync`/`Invoke`, and `Task.Run` bodies that capture a context. `tasks.Add(op)` inside a loop followed by `Task.WhenAll(tasks)`. These are candidate follow-ups once validation shows how common they are.
- Aliasing, where two different symbols refer to the same instance. It is missed by design, which only costs recall.
- Interprocedural analysis. A helper that starts an operation and returns its task isn't followed.

## Decisions

### Resolve a comparable context key per operation

`TryGetContextKey(operation)` returns an `ISymbol` key, or fails:

1. Find the context expression:
   - `SaveChangesAsync`: the invocation's instance, or `this` when the instance is implicit.
   - `FindAsync`: the `DbSet` receiver, then step 2.
   - Queryable extension: `TryAnalyzeSource` on the source argument, then take the origin into step 2.
2. From a `DbSet` expression, take the instance of an `IPropertyReferenceOperation` or `IFieldReferenceOperation` whose instance type derives from `DbContext`. From a `Set<T>()` invocation, take the invocation's instance. Anything else fails, for example a `DbSet` parameter.
3. Unwrap conversions and parentheses, then map the context expression:
   - `ILocalReferenceOperation` → the local, only if no reference to it anywhere in the containing member body is a write target: assignment, compound, ref/out argument, deconstruction, increment. `using var db = …` and `await using var db = …` qualify, because the declaration isn't a later write.
   - `IParameterReferenceOperation` → the parameter, only if it has `RefKind.None` and is never a write target.
   - `IFieldReferenceOperation` whose instance is null (static) or an `IInstanceReferenceOperation` → the field.
   - `IPropertyReferenceOperation` with the same instance rule, when the property is an auto-property, meaning its containing type has a field whose `AssociatedSymbol` is the property → the property.
   - `IInstanceReferenceOperation` → the containing type, standing for `this`.

`EfQueryLocals` wasn't reused for context locals. It rejects `using` declarations, which are the most common way to hold a context. The "never written" check is stricter per local, but it's exactly what identity needs.

### One analyzer, three triggers, one finding per overlapping operation

Register one operation-block action, which runs when a member body contains a core EF operation. Inside it, find resolved `Task.WhenAll`/`Task.WhenAny` invocations for the first two triggers, and walk blocks for the third.

- **Combinator arguments.** Flatten the `Task[]`/`IEnumerable<Task>`/`ReadOnlySpan<Task>` argument: `params` array creation, explicit array creation, or collection expression elements. Collect elements that are core EF operations (after unwrapping conversions) with a context key. For each key, report every operation after the first in argument order.
- **Projected tasks.** A combinator argument that, after unwrapping `ToList`/`ToArray` and following a statically determined local through `EfQueryLocals.TryResolve`, is a resolved `Enumerable.Select` with a lambda selector. Search the selector body, excluding nested lambdas and local functions, for core EF operations whose context key symbol is declared outside the selector. Locals and parameters are checked by declaring syntax span. Fields, auto-properties, and `this` are always outside. Report the first such operation in the selector.
- **Unobserved task local.** For every block, for each statement `var t = <core EF op>;` (a single declarator whose initializer, after unwrapping, is a core operation with a context key), walk the later statements of the block in source order, skipping lambda and local-function bodies. Stop at the first reference to `t`. Report the first core EF operation with the same key found before that stop.

All three triggers run in one operation-block action, so they share a `HashSet<Location>` of reported operations. The triggers don't overlap by construction:

- The combinator trigger never reports the first same-context argument.
- The task-local trigger reports only the first same-context operation after the task local.

So an operation that is both a later argument and after a pending task local has an earlier same-context argument, and that argument is the one the task-local trigger reports. The set is a guard, not a rule.

### Anchor and wording

The span is the full syntax of the overlapping operation's invocation. That includes the query receiver, so the span identifies which query is at fault. Severity is `Warning`, category `Reliability` (as EFD021, the other context-lifetime rule), confidence `high`. Evidence names the resolved method, the context key display (`db`, `this._db`, `this`), the pending operation's syntax or the task local's name, and the shape.

## Risks / Trade-offs

- **A task local's operation could finish before the next one starts**, for example against an in-memory provider or a cached result. → EF Core's concurrency detector still reports overlap reliably only when the first operation is actually in flight. The code is nevertheless incorrect for any real database, so the finding stands at high confidence.
- **A `Select` over a sequence with one element never overlaps.** → This can't be known statically, and it's rare for code that combines tasks. The rule page documents it.
- **Custom `DbContext` pooling or wrappers that hand out new instances through a field-like property.** → Only auto-properties and fields count, and a custom getter is never compared.
- **Operation-block analysis cost.** → The walk is linear in statements per block, and runs only when the block contains a core EF operation.

## Migration Plan

Ship EFD027 enabled by default and register it in the CLI. Add release metadata, the rule page, and README, package README, and CHANGELOG entries, then run the focused and full suites. To roll back, remove the registration, analyzer, tests, fixture, and docs. No persisted data, report field, dependency, or JSON schema changes.
