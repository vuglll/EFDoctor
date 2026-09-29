# Proposal

## Why

A `DbContext` supports one operation at a time. If a second operation starts on the same instance while an earlier one is still running, EF Core throws `InvalidOperationException` ("A second operation was started on this context instance before a previous operation completed"). Whether it throws depends on timing. Code like `await Task.WhenAll(db.Orders.ToListAsync(), db.Customers.ToListAsync())` can pass every local test against a fast database and then fail under production latency, or corrupt the context's state before it throws. The roadmap lists EFD027 as a "Next" candidate: high value, a trigger that can be resolved semantically, and low false-positive risk when the same context instance is proven.

## What Changes

- Add EFD027. It reports an EF Core asynchronous operation that starts while another EF Core asynchronous operation on the **same context instance** is provably still pending. EF Core asynchronous operations are asynchronous query terminals and bulk operations, `SaveChangesAsync`, and `DbSet<T>.FindAsync`, the same set EFD011 and EFD018 recognize. It covers three shapes:
  - **Inline task combinator.** Two or more operations on the same context are passed directly as arguments to `Task.WhenAll` or `Task.WhenAny`.
  - **Unobserved task local.** A task local is initialized with an operation on a context. A later operation on the same context then starts in the same block before the task local is referenced again.
  - **Projected tasks.** `Task.WhenAll` or `Task.WhenAny` receives an `Enumerable.Select` whose selector starts an operation on a context captured from outside the selector. The `Select` may be passed inline, through `ToList`/`ToArray`, or through a statically determined local.
- Prove "the same context instance" by resolved symbol. The context is reached as the operation's receiver, or through the query's proven `DbSet` origin. It must be a local or parameter that is never reassigned, a field or auto-property of `this`, or `this` itself. Contexts reached through arbitrary member chains, method calls, or computed properties are never compared. Separate contexts, such as one per `IDbContextFactory.CreateDbContext()` call, are never the same instance.
- Stay silent when every operation is awaited before the next one starts, when the context is created inside the selector, when a task local is referenced (awaited, passed on, or inspected) before the next operation, and for operations inside lambdas or local functions that may run later.
- Emit one high-confidence, `Warning`-severity `Reliability` finding for each operation that starts while another is pending, anchored on that operation. Its evidence names the context, the pending operation, and the shape. The remediation is to await each operation before starting the next, or to give each concurrent operation its own context through `IDbContextFactory<TContext>`.
- Register EFD027 in CLI workspace analysis. Add analyzer, fixture, suppression, console, JSON, documentation, release-metadata, README, and CHANGELOG coverage. The report schema doesn't change.

## Capabilities

### New Capabilities

- `efd027-concurrent-dbcontext-operations`: Defines detection, context-identity proof, exclusions, evidence, remediation, suppression, and validation for EF Core operations started while another operation on the same context instance is pending.

### Modified Capabilities

None. The existing `cli-analysis` requirement to run every shipped analyzer covers EFD027.

## Impact

The change adds one Roslyn analyzer (`ConcurrentDbContextOperationAnalyzer`), its analyzer tests, an `EFD027.Sample` CLI fixture, a case in the shared test-project fixture, `docs/rules/EFD027.md`, the CLI registration, an `AnalyzerReleases.Unshipped.md` entry, and README, package README, development, suppression, rules-index, roadmap, and CHANGELOG updates. It reuses `EfAsyncOperations`, `EfQueryOperationAnalysis`, and `EfQueryLocals`. The rule is provider-agnostic and local-only. It adds no dependency or code fix, and it doesn't change the versioned JSON schema.
