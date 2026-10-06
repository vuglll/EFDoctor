# Spec Delta

## RENAMED Requirements

- FROM: `### Requirement: Keep first-version analysis local to the expression`
- TO: `### Requirement: Keep analysis within one method`

## MODIFIED Requirements

### Requirement: Keep analysis within one method
EFD002 SHALL analyze the supported invocation and the uses of its result within the containing method, without interprocedural analysis. It SHALL follow the result through one local variable only: a local declared by a declaration statement and initialized by the result, after any `await`, parentheses, or implicit conversions. It SHALL NOT infer an existence test from a result stored in a field, a property, or a variable assigned after its declaration, or from the result of a helper method.

For a count, EFD002 SHALL report the count invocation when the method references that local at least once, every reference is the operand of a supported existence comparison, and no reference is inside a lambda or local function or writes the local. The evidence SHALL name the local.

#### Scenario: Stored count compared later
- **WHEN** `var count = context.Orders.Count(); return count > 0;` is analyzed, or the same with `CountAsync`, a predicate, or any other supported existence comparison
- **THEN** the analyzer reports EFD002 for the count invocation, with high confidence

#### Scenario: Stored count used as a value
- **WHEN** the local is also returned, displayed, used in arithmetic, passed as an argument, compared with another number, read inside a lambda or local function, or assigned again
- **THEN** the analyzer does not report EFD002

#### Scenario: Stored count never read
- **WHEN** the local is never referenced
- **THEN** the analyzer does not report EFD002

#### Scenario: Count stored elsewhere
- **WHEN** a count result is assigned to a field, a property, or a local declared earlier, and that value is compared in a separate expression
- **THEN** the analyzer does not report EFD002

#### Scenario: Count inside another method
- **WHEN** a helper method obtains a count and its caller uses the helper result as an existence test
- **THEN** the analyzer does not report EFD002 based on the caller's comparison

#### Scenario: Comments strings and inactive code
- **WHEN** `Count` or `CountAsync` text occurs only in a comment, string, or inactive preprocessor branch
- **THEN** the analyzer emits no EFD002 diagnostic for that text

## ADDED Requirements

### Requirement: Detect an entity loaded only to test existence
EFD002 SHALL report a semantically resolved `System.Linq.Queryable.FirstOrDefault`, or awaited EF Core `FirstOrDefaultAsync`, with no argument or one predicate, whose source is proven to originate from an EF Core `DbSet<TEntity>` or `DbContext.Set<TEntity>()` and whose element type is `TEntity`, when its result is used only for a null check. A null check is the operand of `== null` or `!= null` with a built-in or compiler-synthesized operator, or the value of an `is null` or `is not null` pattern. The result is used only for a null check when it is that operand directly, or when it initializes a local declared by a declaration statement and every reference to the local, at least one, is a null check outside any lambda or local function.

These findings SHALL have medium confidence, be located on the complete invocation, name the call and any local in the evidence, state that the saving is at most one row's columns, materialization, and change tracking, recommend `Any` or `AnyAsync` with the same predicate, negated where the code tests for `null`, and name the case where the entity is loaded so that it is tracked.

#### Scenario: Inline null check
- **WHEN** `context.Orders.FirstOrDefault() != null` or `await context.Orders.FirstOrDefaultAsync(o => o.Paid) is null` is analyzed
- **THEN** the analyzer reports EFD002 for the invocation, with medium confidence, and recommends `Any` or `AnyAsync`

#### Scenario: Stored entity null-checked
- **WHEN** `var order = context.Orders.FirstOrDefault(o => o.Id == id); return order is not null;` is analyzed
- **THEN** the analyzer reports EFD002 for the invocation, and the evidence names local `order`

#### Scenario: Entity is used
- **WHEN** the result or the local is returned, passed as an argument, dereferenced, used with `?.` or `??`, compared with another entity, read inside a lambda, or assigned again
- **THEN** the analyzer does not report EFD002

#### Scenario: Projected query
- **WHEN** the query projects before `FirstOrDefault`, such as `Select(o => o.Note).FirstOrDefault() != null` or `Select(o => o.Customer).FirstOrDefault() is null`
- **THEN** the analyzer does not report EFD002, because the result can be `null` while a row exists

#### Scenario: Other element operators
- **WHEN** the call is `SingleOrDefault`, `LastOrDefault`, `Find`, `First`, a default-value overload of `FirstOrDefault`, or a `FirstOrDefault` over an in-memory or unproven source
- **THEN** the analyzer does not report EFD002

#### Scenario: Reported test file from the proposal
- **WHEN** the twenty methods posted with the rule proposal are analyzed by EFD002 and EFD019
- **THEN** every method has exactly one finding: EFD002 with medium confidence for the null-checked `FirstOrDefault` methods, EFD002 with high confidence for the stored-count methods, and EFD019 for the `ToList` methods, recommending `Any` everywhere except the method that returns the count
