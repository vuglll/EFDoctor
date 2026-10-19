# Proposal

## Why

Several EFDoctor rules have a mechanical remediation that an IDE could apply in one keystroke: `Count() > 0` to `Any()`, a sync call to its awaited async counterpart, a discarding `OrderBy` to `ThenBy`, `ToList().Count()` to `Count()`. The rules ship in `EFDoctor.Analyzers` since 0.4.0, so code fixes can now ship with them. The roadmap lists code fixes under "Possibly later", and the 0.6.0 plan schedules them as Phase 4.

This change adds the code-fix infrastructure, and the first fix: EFD029's `OrderBy` that should be `ThenBy`. EFD002, EFD019, and EFD010 follow in their own changes.

## What Changes

- **New assembly.** Add `EFDoctor.CodeFixes`, a `netstandard2.0` assembly that compiles against Roslyn 4.8 Workspaces, like the analyzers. It holds the `CodeFixProvider`s, refers to rules by their diagnostic ID strings, and has no reference to `EFDoctor.Analyzers`.
- **Packaging.** The `EFDoctor.Analyzers` package carries `EFDoctor.CodeFixes.dll` beside `EFDoctor.Analyzers.dll` in `analyzers/dotnet/cs`. That's the standard layout for analyzer packages: the IDE loads the fixes, and the command-line compiler ignores them. The package still declares no dependencies.
- **First fix, EFD029:** replace the reported `OrderBy` with `ThenBy`, or `OrderByDescending` with `ThenByDescending`. It is offered only when the receiver is already an `IOrderedQueryable<T>`, so the result compiles. That's when the earlier ordering is immediately before, or separated only by `ThenBy`.
- **Tests:** a test harness that applies a fix in an `AdhocWorkspace`, and verifies the resulting source.

## Capabilities

### New Capabilities

- `analyzer-code-fixes`: how code fixes are offered, what each fix produces, and when a fix is withheld.

### Modified Capabilities

- `analyzer-package`: the package also carries `EFDoctor.CodeFixes.dll`.

## Impact

- **Code:**
  - a new `src/EFDoctor.CodeFixes` project, added to the solution;
  - `EFDoctor.Analyzers.csproj` packs its output and builds it first;
  - `OrderByToThenByCodeFixProvider`.
- **Tests:**
  - `CodeFixTestHarness` and the EFD029 fix tests in `EFDoctor.Analyzers.Tests`;
  - the package-content test lists the new assembly.
- **Docs:**
  - `docs/rules/EFD029.md` mentions the fix;
  - `PACKAGE.md`, the README, and the CHANGELOG mention code fixes;
  - the roadmap moves code fixes from "Possibly later".
