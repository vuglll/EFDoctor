# Tasks

## 1. Infrastructure

- [x] 1.1 Add `src/EFDoctor.CodeFixes` (netstandard2.0, Roslyn 4.8 Workspaces), add it to the solution, and pack its DLL from `EFDoctor.Analyzers.csproj`. Verify that `dotnet pack` contains both DLLs, and that the packaged-analyzer test passes with the updated content list.
- [x] 1.2 Add `CodeFixTestHarness` to the analyzer tests. Verify it with the EFD029 tests.

## 2. EFD029 fix

- [x] 2.1 Add `OrderByToThenByCodeFixProvider`, guarded by the `IOrderedQueryable<T>` receiver check. Verify the reduced, descending, static, and fix-all cases, and the withheld case for an unordered receiver, with traced tests that recompile the result.

## 3. Docs

- [x] 3.1 Update `docs/rules/EFD029.md`, `PACKAGE.md`, the README, the roadmap, the development history, and the CHANGELOG. Verify that `RepositoryConsistencyTests` and `openspec validate --strict` pass, and that the denylist grep is clean.
