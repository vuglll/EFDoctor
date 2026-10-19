## ADDED Requirements

### Requirement: Replace a count existence test with Any
The EFD002 fix SHALL rewrite the comparison of a reported `Count` or `CountAsync` invocation with an integral constant:
- `> 0`, `!= 0`, and `>= 1` SHALL become a call to `Any` or `AnyAsync`;
- `== 0`, `<= 0`, and `< 1` SHALL become the logical negation of that call.

Either operand order SHALL be accepted. The fix SHALL keep the call's arguments (the predicate and the cancellation token), its receiver, its reduced or static form, and an enclosing `await` and `ConfigureAwait(...)`, and SHALL replace the whole comparison. It SHALL NOT be offered when the reported invocation is not directly compared with a constant.

#### Scenario: Count compared for existence
- **WHEN** EFD002 reports `db.Orders.Count(o => o.Open)` in `db.Orders.Count(o => o.Open) > 0`
- **THEN** the fix produces `db.Orders.Any(o => o.Open)`

#### Scenario: Emptiness test and reversed operands
- **WHEN** the comparison is `db.Orders.Count() == 0`, or `0 < db.Orders.Count()`
- **THEN** the fix produces `!db.Orders.Any()`, or `db.Orders.Any()`

#### Scenario: Awaited CountAsync
- **WHEN** the comparison is `await db.Orders.CountAsync(o => o.Open, token) >= 1`, or `await db.Orders.CountAsync().ConfigureAwait(false) < 1`
- **THEN** the fix produces `await db.Orders.AnyAsync(o => o.Open, token)`, or `!await db.Orders.AnyAsync().ConfigureAwait(false)`

#### Scenario: Stored count or null-checked FirstOrDefault
- **WHEN** EFD002 reports a count stored in a local, or a `FirstOrDefault` checked for `null`
- **THEN** the fix is not offered
