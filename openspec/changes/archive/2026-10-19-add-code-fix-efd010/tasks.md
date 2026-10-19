# Tasks

## 1. Fix

- [x] 1.1 Add `AwaitAsyncCounterpartCodeFixProvider`: rename, await, parenthesize, add the using directive, and pass the token, with guards for an async context, a lock, the call form, and the counterpart existing. Verify it with traced tests, fix-all, and the withheld shapes, recompiling each result.

## 2. Docs

- [x] 2.1 Update `docs/rules/EFD010.md`, `PACKAGE.md`, the roadmap, the development history, and the CHANGELOG. Verify that `RepositoryConsistencyTests` and `openspec validate --strict` pass, and that the denylist grep is clean.
