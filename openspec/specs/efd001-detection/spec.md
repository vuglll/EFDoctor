# EFD001 Detection Specification

## Purpose

Defines the precise, high-confidence behavior for identifying EF Core save operations that execute within supported loop bodies while avoiding same-name and deferred-execution false positives.

## Requirements

### Requirement: Semantically identify EF Core save operations
The analyzer SHALL report only invocations whose resolved method is an instance `SaveChanges` or `SaveChangesAsync` overload defined by `Microsoft.EntityFrameworkCore.DbContext`, inherited from it, or overridden along a method chain that originates from it. The analyzer MUST use semantic symbol resolution and MUST NOT infer a match from method-name text alone.

#### Scenario: Synchronous EF Core save
- **WHEN** a resolvable `DbContext.SaveChanges` overload is invoked in a supported loop execution scope
- **THEN** the analyzer reports EFD001 for that invocation

#### Scenario: Asynchronous EF Core save
- **WHEN** a resolvable `DbContext.SaveChangesAsync` overload is invoked and awaited or otherwise called in a supported loop execution scope
- **THEN** the analyzer reports EFD001 for that invocation

#### Scenario: Custom DbContext subclass
- **WHEN** `SaveChanges` or `SaveChangesAsync` is invoked through a custom type derived from `DbContext` in a supported loop execution scope
- **THEN** the analyzer reports EFD001 for that invocation

#### Scenario: Overridden save method
- **WHEN** an invoked override has an overridden-method chain that reaches an EF Core `DbContext.SaveChanges` or `SaveChangesAsync` method and the call is in a supported loop execution scope
- **THEN** the analyzer reports EFD001 for that invocation

#### Scenario: Unrelated same-name method
- **WHEN** a method named `SaveChanges` or `SaveChangesAsync` resolves to a type unrelated to EF Core `DbContext`
- **THEN** the analyzer does not report EFD001

#### Scenario: Unresolved invocation
- **WHEN** the compiler cannot resolve the invocation to a relevant EF Core method symbol
- **THEN** the analyzer does not report EFD001

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

### Requirement: Respect executable-scope boundaries
When determining loop ancestry, the analyzer MUST stop at lambda, anonymous-function, and local-function boundaries. A save invocation in such a deferred callable SHALL be reported only when that callable body itself contains a supported loop around the invocation; declaration of the callable inside an outer loop is insufficient.

#### Scenario: Lambda declared inside a loop
- **WHEN** a lambda or anonymous function containing a matching invocation is declared inside a loop but the invocation has no loop ancestor inside that callable body
- **THEN** the analyzer does not report EFD001 for that invocation

#### Scenario: Local function declared inside a loop
- **WHEN** a local function containing a matching invocation is declared inside a loop but the invocation has no loop ancestor inside that local function body
- **THEN** the analyzer does not report EFD001 for that invocation

#### Scenario: Loop inside a deferred callable
- **WHEN** a lambda, anonymous function, or local function contains its own supported loop with a matching invocation
- **THEN** the analyzer reports EFD001 for that invocation

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

### Requirement: Emit one actionable diagnostic per invocation
The analyzer SHALL emit exactly one EFD001 diagnostic for each matching invocation. The diagnostic SHALL have high confidence, identify the invocation source span, explain that saving within a loop can cause repeated database round trips and inhibit effective batching, and avoid asserting that the occurrence is always incorrect.

#### Scenario: Multiple matching calls
- **WHEN** a loop execution scope contains multiple distinct matching invocations
- **THEN** the analyzer emits one EFD001 diagnostic for each invocation and no aggregate duplicate

#### Scenario: Source span accuracy
- **WHEN** an EFD001 diagnostic is emitted
- **THEN** its start and end line and column identify the matching invocation expression using one-based coordinates in user-facing output

#### Scenario: Comments strings and inactive code
- **WHEN** `SaveChanges` text occurs only in a comment, string, or inactive preprocessor branch
- **THEN** the analyzer emits no EFD001 diagnostic for that text

### Requirement: Provide context-aware remediation
Every EFD001 diagnostic SHALL recommend moving work out of the loop or batching changes before a save where appropriate. The guidance MUST acknowledge intentional per-item transactions, immediate generated-key needs, partial-failure boundaries, and strict consistency requirements as legitimate reasons to retain the pattern after review.

#### Scenario: Remediation is presented
- **WHEN** an EFD001 finding is rendered for a developer
- **THEN** the remediation describes a batching-oriented alternative and names the legitimate exception categories without claiming measured impact

### Requirement: Honor standard Roslyn suppression
EFD001 SHALL participate in standard Roslyn diagnostic suppression, including `#pragma warning`, analyzer severity configuration in editor configuration, and `SuppressMessage` where the Roslyn host supports that mechanism. EFDoctor MUST NOT introduce a proprietary suppression store in this change.

#### Scenario: Pragma suppression
- **WHEN** an otherwise matching invocation is covered by a valid `#pragma warning disable EFD001`
- **THEN** the analyzer result consumed by the CLI excludes the diagnostic until the warning is restored

#### Scenario: Editor configuration suppression
- **WHEN** applicable editor configuration sets `dotnet_diagnostic.EFD001.severity = none`
- **THEN** the analyzer result consumed by the CLI excludes EFD001 for that configured scope

#### Scenario: SuppressMessage with justification
- **WHEN** a supported `SuppressMessage` attribute targets EFD001 and records an intentional-use justification
- **THEN** the analyzer result consumed by the CLI excludes the targeted diagnostic
