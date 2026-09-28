# Design

## Context

See [proposal.md](proposal.md) for motivation and the EFD011 and CLI delta specifications for observable behavior.

EFDoctor's analyzers use Roslyn operation analysis, semantic symbol identity, immutable diagnostic properties, concurrent execution, and standard suppression. EFD011 needs to connect a blocking consumer to the immediately preceding EF Core asynchronous operation without broad task data flow, distinguish framework blocking APIs from same-named members, and handle both `Task<T>` and `ValueTask<T>` operation shapes while retaining the analyzer project's `netstandard2.0` target.

## Goals / Non-Goals

**Goals:**

- Resolve both the EF Core operation and the blocking consumer semantically.
- Cover direct `.Result`, parameterless `.Wait()`, and `.GetAwaiter().GetResult()` patterns, including configured awaiters.
- Emit one high-confidence diagnostic at the complete blocking expression with actionable structured properties.
- Preserve existing CLI, reporting, suppression, privacy, and concurrent-analysis behavior.

**Non-Goals:**

- Follow tasks or value tasks through variables, members, parameters, returns, helpers, or interprocedural control flow.
- Diagnose every synchronous EF Core API, generic sync-over-async outside EF Core, or timeout/cancellation `Wait` overloads.
- Prove an actual deadlock, thread-pool starvation event, request context, or runtime performance cost.
- Add a code fix, rewrite callers to async, execute target code, or inspect a database.

## Decisions

### 1. Start from recognized blocking consumers and trace one direct expression chain

The analyzer will register for property-reference and invocation operations. A `.Result` property reference, parameterless `Wait()` invocation, or `GetResult()` invocation becomes a candidate only when its symbol belongs to the BCL task/awaiter family. From that consumer, a small walker unwraps implicit conversions, parentheses, and the recognized `GetAwaiter` and `ConfigureAwait` adapters to find the source invocation.

This consumer-first shape naturally emits one diagnostic per block and makes the reported location deterministic. It avoids duplicate findings on the nested EF invocation and avoids treating any use of an async method as blocking.

Alternative considered: analyze every EF async invocation and search its parents. That couples source classification to numerous parent shapes and is more prone to duplicate or partial locations.

### 2. Use an explicit EF Core async operation allowlist by symbol family

Eligible sources will be resolved methods in these semantic families: asynchronous terminal methods on `EntityFrameworkQueryableExtensions`, `DbContext.SaveChangesAsync` including overrides, and `DbSet<TEntity>.FindAsync`. Method-name suffixes alone are insufficient, and arbitrary methods in the `Microsoft.EntityFrameworkCore` namespace are not automatically trusted.

The initial allowlist concentrates on operations that can perform database I/O and are readily exercised by fixtures. Additional EF operation families can be added later with dedicated precision evidence.

Alternative considered: accept any method ending in `Async` from an EF namespace. That would include non-I/O helpers and make namespace spoofing or future API additions silently broaden the rule.

### 3. Verify framework blocking APIs by containing symbols

`.Result` must resolve to `Task<TResult>.Result` or `ValueTask<TResult>.Result`. `.Wait()` must resolve to the parameterless instance method on `Task`; overloads with timeout or cancellation arguments are intentionally excluded. `GetResult()` must resolve to the appropriate task or value-task awaiter, reached through its real `GetAwaiter()` operation. `ConfigureAwait(bool)` may appear between the source and awaiter because this remains an immediate, semantically transparent chain.

Alternative considered: match member names and syntax. That would report user-defined `Result`, `Wait`, `GetAwaiter`, or `GetResult` patterns and violate the project's semantic precision standard.

### 4. Keep the origin proof expression-local

The source walker stops unless it reaches the EF operation in the same expression. It does not resolve locals, fields, properties, parameters, returns, aliases, or helper results. This creates known false negatives but ensures every finding can name the EF operation that the reported blocking consumer directly wraps.

Alternative considered: local data-flow tracking. It would expand scope substantially and require assignment, mutation, branch, and alias reasoning before EFD011's simpler high-confidence value is validated.

### 5. Emit qualified, structured evidence through existing contracts

The descriptor title will be `Synchronous blocking on an EF Core async operation`, severity warning, confidence high, and documentation key `EFD011`. Evidence will name the resolved EF operation and blocking API. Impact language will describe thread occupation, reduced scalability, thread-pool-starvation risk, and context-dependent deadlock risk without asserting that a failure occurred. Remediation will prefer `await` plus async propagation and acknowledge deliberate synchronous integration boundaries.

### 6. Integrate additively through existing CLI and test infrastructure

EFD011 adds one analyzer registration and one unshipped release row. Existing finding mapping, sorting, de-duplication, console/JSON schema version `1`, exit codes, and Roslyn suppression remain unchanged.

Focused analyzer tests will exceed ten positive and ten negative cases. A dedicated fixture will contain unsuppressed, awaited, and pragma/editor-config/attribute-suppressed cases; end-to-end tests will verify exact coordinates, structured properties, ordering, and absence of suppressed findings.

## Risks / Trade-offs

- **[Stored tasks are missed]** → Keep the direct-expression boundary explicit in documentation and revisit local flow only after precision data justifies it.
- **[EF Core adds async operation families]** → Use an intentional allowlist and expand it with tests instead of silently matching namespace or suffix patterns.
- **[Deadlock risk varies by synchronization context]** → Use qualified language and emphasize guaranteed thread blocking rather than claiming every occurrence deadlocks.
- **[ValueTask and configured-await operation trees vary]** → Cover `FindAsync`, direct `ValueTask.Result`, and configured awaiters with focused operation-based fixtures.
- **[Async propagation can be invasive]** → Recommend propagation as the default but document deliberate boundary isolation as the alternative.

## Migration Plan

1. Add the EFD011 analyzer and focused semantic fixtures without changing existing analyzers.
2. Register it in the CLI, add a dedicated fixture, and verify console/JSON reporting and suppression.
3. Add release metadata and documentation, then run the full solution regression and strict OpenSpec validation.

Rollback is additive: remove the analyzer registration, source, tests, fixture, documentation, release row, and EFD011 README sections. No persisted data, public JSON schema, or external migration is involved.
