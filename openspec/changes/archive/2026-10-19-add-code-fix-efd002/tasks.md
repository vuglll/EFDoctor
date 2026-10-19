# Tasks

## 1. Fix

- [x] 1.1 Add `CountToAnyCodeFixProvider`: find the reported invocation, its `await`/`ConfigureAwait` wrapper and comparison, rename `Count`/`CountAsync`, and negate for emptiness tests. Verify it with traced tests for every comparison form, async, static syntax, fix-all, and the withheld shapes, recompiling each result.

## 2. Docs

- [x] 2.1 Update `docs/rules/EFD002.md`, `PACKAGE.md`, the roadmap, the development history, and the CHANGELOG. Verify that `RepositoryConsistencyTests` and `openspec validate --strict` pass, and that the denylist grep is clean.
