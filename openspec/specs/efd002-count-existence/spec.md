# EFD002 Count Existence Specification

## Purpose

Defines high-confidence detection of EF Core count queries whose result is used only to determine whether any matching row exists, together with precise exclusions and actionable guidance.

## Requirements

### Requirement: Semantically identify supported EF Core count operations
EFD002 SHALL match a synchronous `System.Linq.Queryable.Count` invocation only when its query source can be established from the analyzed expression as originating from an EF Core `DbSet`. EFD002 SHALL match asynchronous `CountAsync` invocations only when the resolved method is an EF Core `EntityFrameworkQueryableExtensions.CountAsync` overload. Matching MUST use resolved method and type symbols rather than method-name text.

#### Scenario: Direct DbSet count
- **WHEN** `Queryable.Count` is invoked on a `DbSet` and the result is used in a supported existence test
- **THEN** the analyzer reports EFD002 for the count invocation

#### Scenario: Composed DbSet query count
- **WHEN** `Queryable.Count` is invoked after a semantically resolved query chain whose source expression is a `DbSet` and the result is used in a supported existence test
- **THEN** the analyzer reports EFD002 for the count invocation

#### Scenario: EF Core asynchronous count
- **WHEN** a resolved EF Core `CountAsync` overload is awaited and its result is used in a supported existence test
- **THEN** the analyzer reports EFD002 for the count invocation

#### Scenario: Predicate overload
- **WHEN** a supported synchronous or asynchronous count predicate overload is used only for a supported existence test
- **THEN** the analyzer reports EFD002 and recommends the corresponding predicate overload of `Any` or `AnyAsync`

#### Scenario: LINQ-to-Objects count
- **WHEN** a count invocation resolves to `System.Linq.Enumerable.Count`
- **THEN** the analyzer does not report EFD002

#### Scenario: Unrelated or unresolved method
- **WHEN** `Count` or `CountAsync` resolves to an unrelated method, or does not successfully resolve to a supported method symbol
- **THEN** the analyzer does not report EFD002

#### Scenario: Unproven synchronous query provider
- **WHEN** `Queryable.Count` is invoked on an arbitrary `IQueryable` whose analyzed source expression cannot be traced to an EF Core `DbSet`
- **THEN** the analyzer does not report EFD002

### Requirement: Recognize direct existence comparisons
EFD002 SHALL report when a supported count result participates directly in a binary equality or relational comparison that is logically equivalent, for a non-negative count, to testing whether the query has any rows or no rows. Supported forms SHALL include comparison with a compile-time integer value of zero or one: `count > 0`, `count != 0`, `count >= 1`, `count == 0`, `count <= 0`, and `count < 1`, plus their operand-reversed equivalents. Parentheses and implicit conversions SHALL NOT prevent a match.

#### Scenario: Positive existence comparison
- **WHEN** a supported count is compared using `> 0`, `!= 0`, or `>= 1`, including an operand-reversed equivalent
- **THEN** the analyzer reports EFD002 for the count invocation

#### Scenario: Empty-set comparison
- **WHEN** a supported count is compared using `== 0`, `<= 0`, or `< 1`, including an operand-reversed equivalent
- **THEN** the analyzer reports EFD002 for the count invocation

#### Scenario: Constant-folded boundary
- **WHEN** the zero or one comparison operand is a compile-time integer constant rather than a literal
- **THEN** the analyzer applies the same supported existence-test semantics

#### Scenario: Awaited asynchronous comparison
- **WHEN** an awaited supported `CountAsync` result is wrapped in parentheses or implicit conversions before a supported comparison
- **THEN** the analyzer reports EFD002 for the `CountAsync` invocation

#### Scenario: Exact-count comparison
- **WHEN** a count result is compared in a way that distinguishes an exact count, such as `count == 1` or `count > 1`
- **THEN** the analyzer does not report EFD002

#### Scenario: Count used as a value
- **WHEN** a supported count result is returned, displayed, used in arithmetic, or otherwise consumed without a supported direct existence comparison
- **THEN** the analyzer does not report EFD002

### Requirement: Keep first-version analysis local to the expression
EFD002 SHALL analyze the count invocation and its containing comparison without interprocedural analysis or general value-flow tracking. It SHALL NOT infer an existence test from a count stored in a variable and compared by a later expression.

#### Scenario: Stored count compared later
- **WHEN** a count result is assigned to a local, field, or property and that value is compared in a separate expression
- **THEN** the analyzer does not report EFD002 in this version

#### Scenario: Count inside another method
- **WHEN** a helper method obtains a count and its caller uses the helper result as an existence test
- **THEN** the analyzer does not report EFD002 based on the caller's comparison

#### Scenario: Comments strings and inactive code
- **WHEN** `Count` or `CountAsync` text occurs only in a comment, string, or inactive preprocessor branch
- **THEN** the analyzer emits no EFD002 diagnostic for that text

### Requirement: Emit one actionable diagnostic per matching count invocation
The analyzer SHALL emit exactly one EFD002 diagnostic for each matching count invocation. The diagnostic SHALL have warning severity and high confidence, identify the full count invocation source span, and provide the rule ID, title, message, evidence, likely impact, suggested remediation, and EFD002 documentation reference required by the shared finding contract.

#### Scenario: Multiple matching count calls
- **WHEN** source contains multiple distinct matching count invocations
- **THEN** the analyzer emits one EFD002 diagnostic for each invocation without aggregate or duplicate diagnostics

#### Scenario: Source span accuracy
- **WHEN** an EFD002 diagnostic is rendered in console or JSON output
- **THEN** its one-based start and end coordinates identify the matching count invocation

#### Scenario: Mixed EFD001 and EFD002 findings
- **WHEN** a scan produces findings from both rules
- **THEN** both output formats include all findings in the shared deterministic ordering

### Requirement: Explain impact without overstating certainty
Every EFD002 finding SHALL explain that counting rows solely to test existence can require the database to process more matches than an existence query that can stop after the first match. The message MUST NOT claim that every count query scans every row, prescribe a measured performance gain, or report when the exact count is observably required.

#### Scenario: Actionable existence guidance
- **WHEN** an EFD002 finding is presented
- **THEN** its evidence identifies the resolved count operation and existence comparison, and its remediation recommends the equivalent `Any` or `AnyAsync` form while preserving an existing predicate

#### Scenario: Exact count is required
- **WHEN** the surrounding expression observably requires the numeric count rather than only existence
- **THEN** EFD002 remains silent and does not suggest replacing the operation

### Requirement: Honor standard Roslyn suppression
EFD002 SHALL participate in standard Roslyn diagnostic suppression, including `#pragma warning`, analyzer severity configuration in editor configuration, and `SuppressMessage` where the Roslyn host supports that mechanism. EFDoctor MUST NOT add a proprietary suppression store for EFD002.

#### Scenario: Pragma suppression
- **WHEN** a matching invocation is covered by a valid `#pragma warning disable EFD002`
- **THEN** the analyzer result consumed by the CLI excludes that diagnostic until the warning is restored

#### Scenario: Editor configuration suppression
- **WHEN** applicable editor configuration sets `dotnet_diagnostic.EFD002.severity = none`
- **THEN** the analyzer result consumed by the CLI excludes EFD002 for that configured scope

#### Scenario: SuppressMessage with justification
- **WHEN** a supported `SuppressMessage` attribute targets EFD002 and records why the count form is intentional
- **THEN** the analyzer result consumed by the CLI excludes the targeted diagnostic

### Requirement: Preserve shared CLI and report behavior
EFD002 findings SHALL flow through the existing local-only CLI, console renderer, schema-versioned JSON renderer, deterministic ordering, path validation, and exit-code contracts without introducing a new command or report schema version. A successful scan containing EFD002 findings SHALL use the existing findings exit code.

#### Scenario: Console scan with EFD002
- **WHEN** the CLI analyzes a valid project or solution containing a matching EFD002 expression using console output
- **THEN** it renders the complete actionable finding and returns the successful-scan-with-findings exit code

#### Scenario: JSON scan with EFD002
- **WHEN** the CLI analyzes a valid project or solution containing a matching EFD002 expression using JSON output
- **THEN** it emits the finding in the existing schema-versioned envelope without color or non-JSON stdout text and returns the successful-scan-with-findings exit code

#### Scenario: No matching findings
- **WHEN** a valid scan produces neither EFD001 nor EFD002 findings
- **THEN** the CLI preserves the successful-scan-with-no-findings exit code
