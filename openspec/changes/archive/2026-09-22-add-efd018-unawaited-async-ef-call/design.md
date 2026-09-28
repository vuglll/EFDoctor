# Design

## Context

EFD011 (`SyncOverAsyncEfOperationAnalyzer`) already recognizes a set of resolved EF Core asynchronous operations: any `*Async` method on `EntityFrameworkQueryableExtensions`, `DbContext.SaveChangesAsync` (walking overrides), and `DbSet<T>.FindAsync`. It reports only direct synchronous blocking. On EF Core 10, which the repository targets, `ExecuteUpdateAsync` and `ExecuteDeleteAsync` are declared on `EntityFrameworkQueryableExtensions`, so the existing set already includes them. `AddAsync`/`AddRangeAsync` and the relational `RelationalDatabaseFacadeExtensions.ExecuteSql*Async` methods are not in the set. `EfQueryOperationAnalysis` provides method normalization, source extraction, and unwrapping helpers. See `proposal.md` for motivation and the EFD018 and CLI spec deltas for observable behavior.

The C# compiler's CS4014 warning overlaps with EFD018 in `async` methods. Fixture projects build with `TreatWarningsAsErrors`, so EFD018 fixtures must allow CS4014 and EFD018 as warnings.

## Goals / Non-Goals

**Goals:**

- Recognize the same core EF Core async operations as EFD011, plus the tracking and relational raw-SQL operations named in the proposal, by resolved symbols only.
- Detect a discard from the operation's immediate syntactic and operation context, with no data-flow analysis.
- Keep EFD011 behavior unchanged, and never report the same expression under both rules.

**Non-Goals:**

- Tracking tasks through locals, fields, collections, or helper methods to prove they are eventually awaited or dropped.
- Diagnosing `async void` methods, unobserved tasks passed to arbitrary APIs, or `Task.Run` wrappers around EF calls.
- Diagnosing non-EF async APIs, transactions (`BeginTransactionAsync`, `CommitAsync`), or `DbConnection` operations.
- A code fix that inserts `await`.

## Decisions

### Start from the resolved EF operation and inspect its consumer

Register a compilation-start invocation action after resolving `DbContext`, `DbSet<T>`, `EntityFrameworkQueryableExtensions`, and the optional `RelationalDatabaseFacadeExtensions`. For each invocation that resolves to a supported EF async operation, walk outward through parentheses, implicit conversions, a single `ConfigureAwait(bool)` or `ConfigureAwait(ConfigureAwaitOptions)` call on a task-like receiver, and a null-conditional access whose `WhenNotNull` is the task. The first consumer decides the outcome:

- `IExpressionStatementOperation` → discarded (statement form). When that statement is the implicit body of an expression-bodied lambda converted to a void-returning delegate, the form is reported as a void-returning lambda.
- `ISimpleAssignmentOperation` whose target is `IDiscardOperation` → discarded (discard-assignment form).
- Anything else (await, return, argument, local or member assignment, a member access such as `.Result`, `.Wait()`, `.GetAwaiter()`, or `.ContinueWith`) → observed or delegated, so no report.

Starting from the EF operation keeps symbol resolution in one place and makes every exclusion the default outcome. The alternative, starting from every expression statement and discard assignment and searching inward, was rejected because it would duplicate the unwrapping logic in reverse and would easily misclassify composed task expressions.

Because a blocking consumer (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`) is never a discard consumer, EFD011 and EFD018 cannot report the same operation.

### Share the core operation recognizer with EFD011

Extract EFD011's `TryGetEfOperation` classification into a small internal `EfAsyncOperations` helper that exposes the core set (queryable-extension `*Async`, `SaveChangesAsync` with override walking, `DbSet<T>.FindAsync`). EFD011 calls only the core set, so its behavior is unchanged. EFD018 calls the core set plus an extended check for `AddAsync`/`AddRangeAsync` on `DbContext` or `DbSet<T>` and `*Async` methods on `RelationalDatabaseFacadeExtensions` whose names start with `ExecuteSql`. The existing EFD011 test suite is the regression guard. If extraction creates coupling, a local copy in the new analyzer is acceptable.

Widening EFD011 to the extended set in the same change was rejected: blocking on `AddAsync` is a different, rarer shape, and widening it would change EFD011's shipped contract without its own proposal.

### Report the complete discarded expression

The diagnostic location is the expression statement's expression (for example `context.SaveChangesAsync().ConfigureAwait(false)` or `context?.SaveChangesAsync()`), the full discard assignment `_ = context.SaveChangesAsync()`, or the lambda body expression. Evidence names the operation (formatted like EFD011's `CSharpErrorMessageFormat` display) and the discard form. Severity is warning and confidence is high, because both the EF symbol and the discard shape are proven. The category is `Correctness`, matching EFD017.

Remediation leads with awaiting and propagating async. For deliberate background work, it recommends a separately scoped context (for example from `IDbContextFactory<TContext>` or a new DI scope) whose task is observed and logged, rather than an explicit discard.

### Keep CLI integration and policies centralized

Register the analyzer once in `WorkspaceAnalyzer`. Mapping, deterministic ordering, console and JSON rendering, exit codes, privacy behavior, standard Roslyn suppression, and uniform test-project downgrade stay unchanged. A buildable EFD018 fixture covers positive, excluded, and suppressed shapes at process level. The shared test-project fixture gains an EFD018 case.

## Risks / Trade-offs

- [Intentional fire-and-forget with an explicit `_ =`] → Still reported, because a discarded EF task on a shared context is almost never safe. Documentation shows the scoped-context alternative and standard suppression with a recorded justification.
- [Overlap with CS4014 in async methods] → Accepted: EFD018 adds EF-specific evidence and remediation, and covers synchronous methods and explicit discards, which CS4014 does not. Documentation explains the relationship.
- [Void-returning lambdas passed to APIs such as `List<T>.ForEach`] → Reported, since the task is genuinely discarded. Lambdas converted to task-returning delegates remain clean.
- [Future EF Core versions move or add async APIs] → Match resolved containing types and validate against the repository's EF Core 10 package. Unknown methods remain unreported.
- [Shared-helper extraction regresses EFD011] → Keep the core set identical and run the existing EFD011 suite unchanged.

## Migration Plan

Ship EFD018 enabled by default with the other analyzers, add its release metadata and rule documentation, register it in the CLI, and validate focused and full-suite behavior. Rollback consists of removing the EFD018 registration and its implementation, tests, and docs, and inlining the shared recognizer back into EFD011 if desired. No persisted data, public report field, network service, dependency migration, or JSON schema change is involved.
