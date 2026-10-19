# Proposal

## Why

EFD002 reports `Count()` used only to test existence, as in `db.Orders.Count(o => o.Open) > 0`. The fix is mechanical: `Any(o => o.Open)`, or its negation. This adds the EFD002 code fix to `EFDoctor.Analyzers`, the second fix of Phase 4.

## What Changes

- Add the **EFD002 code fix**, "Use Any". It rewrites an inline existence comparison of a reported `Count` or `CountAsync` call:
  - **Existence tests:** `> 0`, `!= 0`, and `>= 1`, either way round, become `Any(...)`/`AnyAsync(...)`.
  - **Emptiness tests:** `== 0`, `<= 0`, and `< 1` become `!Any(...)`/`!AnyAsync(...)`.
  - **Preserved:** the predicate, the cancellation token, `await`, `ConfigureAwait`, and the reduced or static call form.
- The fix isn't offered for EFD002's other shapes: a count stored in a local, and a `FirstOrDefault` checked for `null`. Those span several statements.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `analyzer-code-fixes`: adds the EFD002 requirement.

## Impact

- **Code:** `CountToAnyCodeFixProvider` in `EFDoctor.CodeFixes`.
- **Tests:** fix tests, including fix-all and the withheld shapes, recompiling every result.
- **Docs:** `docs/rules/EFD002.md`, `PACKAGE.md`'s code-fix list, the roadmap, the development history, and the CHANGELOG.
