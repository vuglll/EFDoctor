## MODIFIED Requirements

### Requirement: Recognize supported loop execution scopes
The analyzer SHALL treat `for`, `foreach`, `await foreach`, `while`, and `do`/`while` bodies as supported loop execution scopes. A `foreach` or `await foreach` whose collection expression has an option type, a generic type with one type parameter named `Option`, `Maybe`, or `Optional`, SHALL NOT be a loop execution scope, because it runs at most once; the analyzer SHALL look for an enclosing supported loop instead. A matching invocation SHALL remain in the loop execution scope when nested in ordinary blocks such as conditionals, `try` statements, `using` statements, switches, or nested loops. A conditional whose condition is a batch threshold, as defined in "Do not report saves that run once per batch", is not an ordinary block for this purpose.

#### Scenario: Each supported loop form
- **WHEN** a matching invocation occurs directly within any supported loop form
- **THEN** the analyzer reports EFD001

#### Scenario: Nested ordinary control-flow block
- **WHEN** a matching invocation occurs inside an `if` whose condition is not a batch threshold, a `try`, or another ordinary control-flow block nested beneath a supported loop
- **THEN** the analyzer reports EFD001

#### Scenario: Loop over an option value
- **WHEN** a matching invocation is in the body of `foreach (var item in maybeItem)`, where `maybeItem` is an `Option<T>`, `Maybe<T>`, or `Optional<T>`, and no other supported loop encloses it
- **THEN** the analyzer does not report EFD001

#### Scenario: Option loop inside a per-item loop
- **WHEN** a loop over an option value is nested inside a per-item `foreach`, or a per-item loop is nested inside it, and a matching invocation is in the inner body
- **THEN** the analyzer reports EFD001

#### Scenario: Save outside a loop
- **WHEN** a matching invocation has no supported loop ancestor in its current executable scope
- **THEN** the analyzer does not report EFD001

#### Scenario: Method declaration near loop syntax
- **WHEN** source contains a method declaration near or within surrounding loop syntax but no matching invocation executes in that loop scope
- **THEN** the analyzer does not report EFD001
