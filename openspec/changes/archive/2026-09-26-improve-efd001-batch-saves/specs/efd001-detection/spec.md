## ADDED Requirements

### Requirement: Do not report saves that run once per batch
The analyzer SHALL NOT report a matching invocation when its innermost enclosing supported loop saves once per batch rather than once per item. The innermost loop saves once per batch when at least one of these holds:
- **Batch iteration variable:** the loop is a `foreach` or `await foreach` whose element type is a collection other than `string`.
- **Page declared by the loop condition:** the loop is a `while`, `do`, or `for` whose condition declares or assigns a local of a collection type other than `string`.
- **Page loaded in the loop body:** the loop is a `while`, `do`, or `for` whose body declares a local of a collection type other than `string`, and iterates that local with a `foreach` that ends before the invocation. The `foreach` may iterate the local directly, or a method chain over it such as `page.Where(...)` or `page.AsAsyncEnumerable()`.
- **Threshold flush:** the invocation is inside the `then` branch of an `if`, within the loop, whose condition contains a batch threshold.

A batch threshold is a `>=`, `>`, or `==` comparison where one side is either of these:
- a local incremented within the loop (by `++`, `--`, `+=`, or `-=`), or a `%` expression over such a local;
- the `Count` or `Length` of a collection.

Only the innermost enclosing loop SHALL be considered. A per-item loop nested inside a batch loop is not a batch loop. An `if` whose condition is not a batch threshold SHALL NOT exempt the invocation.

#### Scenario: Save per chunk
- **WHEN** a matching invocation is in the body of `foreach (var chunk in items.Chunk(100))`
- **THEN** the analyzer does not report EFD001

#### Scenario: Save per page declared by the loop condition
- **WHEN** a matching invocation is in the body of `while (pager.ReadNextPage(out var page))`, where `page` is a collection
- **THEN** the analyzer does not report EFD001

#### Scenario: Save per page loaded in the loop body
- **WHEN** a `while (true)` body loads a page into a collection local, iterates it with `foreach`, and then calls a matching save
- **THEN** the analyzer does not report EFD001

#### Scenario: Threshold flush with a counter
- **WHEN** a matching invocation is inside `if (processed >= BatchSize)` in a per-item loop that increments `processed`
- **THEN** the analyzer does not report EFD001

#### Scenario: Threshold flush with a modulo or a buffer count
- **WHEN** a matching invocation is inside `if (index % 100 == 0)` for a loop counter `index`, or inside `if (buffer.Count >= 100)`
- **THEN** the analyzer does not report EFD001

#### Scenario: Per-item loop nested in a batch loop
- **WHEN** a matching invocation is in the body of `foreach (var item in chunk)`, which is itself nested in `foreach (var chunk in items.Chunk(100))`
- **THEN** the analyzer reports EFD001

#### Scenario: Ordinary condition is not a batch threshold
- **WHEN** a matching invocation in a per-item loop is guarded by a condition that is not a batch threshold, such as `if (item.IsTransient)` or `if (i > 0)` on the iteration variable
- **THEN** the analyzer reports EFD001

## MODIFIED Requirements

### Requirement: Recognize supported loop execution scopes
The analyzer SHALL treat `for`, `foreach`, `await foreach`, `while`, and `do`/`while` bodies as supported loop execution scopes. A matching invocation SHALL remain in the loop execution scope when nested in ordinary blocks such as conditionals, `try` statements, `using` statements, switches, or nested loops. A conditional whose condition is a batch threshold, as defined in "Do not report saves that run once per batch", is not an ordinary block for this purpose.

#### Scenario: Each supported loop form
- **WHEN** a matching invocation occurs directly within any supported loop form
- **THEN** the analyzer reports EFD001

#### Scenario: Nested ordinary control-flow block
- **WHEN** a matching invocation occurs inside an `if` whose condition is not a batch threshold, a `try`, or another ordinary control-flow block nested beneath a supported loop
- **THEN** the analyzer reports EFD001

#### Scenario: Save outside a loop
- **WHEN** a matching invocation has no supported loop ancestor in its current executable scope
- **THEN** the analyzer does not report EFD001

#### Scenario: Method declaration near loop syntax
- **WHEN** source contains a method declaration near or within surrounding loop syntax but no matching invocation executes in that loop scope
- **THEN** the analyzer does not report EFD001
