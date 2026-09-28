# Proposal

## Why

An asynchronous EF Core call whose returned task is discarded runs as fire-and-forget: the save or query may not finish before the context is reused or disposed, its exceptions go unobserved, and overlapping operations on one `DbContext` can fail with concurrent-use errors or silently lose data. The compiler's CS4014 warning covers only some of these cases. It does not fire in synchronous methods, and an explicit `_ =` discard silences it. EFD018 is the product brief's next recommended rule after EFD017 because a discarded EF Core task has a semantically precise shape with very few legitimate exceptions.

## What Changes

- Add EFD018 detection for semantically resolved EF Core asynchronous operations whose task result is directly discarded, either as a standalone expression statement or through an explicit discard assignment (`_ = ...`), including when the task is first wrapped in `ConfigureAwait(...)` or reached through null-conditional access.
- Cover the async operations EFD011 already recognizes: asynchronous `EntityFrameworkQueryableExtensions` operations, including query terminals and `ExecuteUpdateAsync`/`ExecuteDeleteAsync`, `DbContext.SaveChangesAsync`, and `DbSet<T>.FindAsync`. Add the tracking operations `AddAsync`/`AddRangeAsync` on `DbContext` and `DbSet<T>`, and the relational `Database.ExecuteSql*Async` operations when the relational assembly is referenced.
- Do not report tasks that are awaited, returned, stored, passed as arguments, composed with other task APIs, or synchronously blocked (the last is EFD011's domain), or methods whose names only resemble EF Core APIs.
- Emit a high-confidence correctness warning on the complete discarded expression. Evidence names the resolved operation and the discard form. Remediation recommends awaiting the operation and propagating async, and, for intentional background work, using a separately scoped context whose task is observed.
- Register EFD018 in CLI workspace analysis and add analyzer, fixture, suppression, console, JSON, documentation, release-metadata, and regression coverage without changing the report schema.

## Capabilities

### New Capabilities

- `efd018-unawaited-async-ef-call`: Defines conservative semantic detection, exclusions, evidence, remediation, suppression, and validation coverage for discarded EF Core asynchronous operation tasks.

### Modified Capabilities

- `cli-analysis`: Requires EFD018 to run in CLI analysis and participate in the existing reporting, ordering, suppression, privacy, test-project downgrade, and exit-code contracts.

## Impact

The change adds one Roslyn analyzer, analyzer and CLI fixtures and tests, EFD018 rule documentation, CLI analyzer registration, release metadata, and README status updates. It may extract a small shared helper for recognizing resolved EF Core async operations, provided EFD011 behavior is unchanged. It reuses the current Roslyn and EF Core test dependencies, remains provider-agnostic and local-only, adds no code fix, and does not change the versioned JSON schema.
