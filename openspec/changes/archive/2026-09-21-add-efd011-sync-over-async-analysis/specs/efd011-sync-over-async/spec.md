# Spec Delta

## Purpose

Define a high-confidence diagnostic for code that synchronously blocks on a directly invoked EF Core asynchronous operation.

## ADDED Requirements

### Requirement: Semantically identify EF Core asynchronous operations
EFD011 SHALL recognize supported asynchronous EF Core operations by resolved method and containing-type symbols rather than method-name text. Supported operations SHALL include EF Core asynchronous query execution, `DbContext.SaveChangesAsync`, and `DbSet.FindAsync` when consumed directly by a recognized blocking pattern.

#### Scenario: Async query terminal
- **WHEN** a resolved EF Core asynchronous query terminal such as `ToListAsync`, `FirstAsync`, or `CountAsync` is consumed directly by a recognized blocking pattern
- **THEN** EFD011 treats the operation as an eligible EF Core asynchronous call

#### Scenario: SaveChangesAsync
- **WHEN** `SaveChangesAsync` resolves to EF Core `DbContext` or an override on a derived context and is consumed directly by a recognized blocking pattern
- **THEN** EFD011 treats the operation as eligible

#### Scenario: FindAsync
- **WHEN** `FindAsync` resolves to EF Core `DbSet<TEntity>` and is consumed directly by a recognized blocking pattern
- **THEN** EFD011 treats the operation as eligible

#### Scenario: Same-named non-EF method
- **WHEN** an asynchronous same-named method resolves outside supported EF Core types
- **THEN** EFD011 does not report

### Requirement: Detect direct synchronous blocking consumption
EFD011 SHALL report direct consumption of an eligible EF Core asynchronous operation through the `Task<TResult>.Result` property, parameterless `Task.Wait()`, or `GetAwaiter().GetResult()`. It SHALL also recognize `ConfigureAwait(...)` between the EF operation and `GetAwaiter()`, and SHALL emit one diagnostic for each blocking consumption.

#### Scenario: Result property
- **WHEN** code directly reads `.Result` from an eligible EF Core asynchronous operation
- **THEN** EFD011 reports the complete `.Result` access

#### Scenario: Parameterless Wait
- **WHEN** code directly invokes parameterless `.Wait()` on an eligible EF Core asynchronous operation
- **THEN** EFD011 reports the complete `.Wait()` invocation

#### Scenario: Awaiter GetResult
- **WHEN** code directly invokes `.GetAwaiter().GetResult()` on an eligible EF Core asynchronous operation
- **THEN** EFD011 reports the complete `.GetAwaiter().GetResult()` invocation

#### Scenario: Configured awaiter GetResult
- **WHEN** code invokes `.ConfigureAwait(true)` or `.ConfigureAwait(false)` on an eligible EF Core asynchronous operation and then calls `.GetAwaiter().GetResult()`
- **THEN** EFD011 reports the complete blocking consumption

#### Scenario: Multiple blocking consumptions
- **WHEN** a source file contains multiple independent eligible blocking consumptions
- **THEN** EFD011 emits one finding for each consumption

### Requirement: Preserve conservative expression and overload boundaries
EFD011 SHALL analyze only direct expression chains and SHALL NOT follow tasks or value tasks through locals, fields, properties, parameters, return values, helper methods, or general data flow. It SHALL NOT report ordinary `await`, nonblocking task composition, or `Wait` overloads with timeout or cancellation arguments.

#### Scenario: Proper await
- **WHEN** an eligible EF Core asynchronous operation is awaited
- **THEN** EFD011 does not report

#### Scenario: Stored task
- **WHEN** an eligible EF Core asynchronous operation is assigned to a local or member and blocked later
- **THEN** EFD011 does not report in the first version because the blocking expression no longer proves its EF origin directly

#### Scenario: Wait overload with control arguments
- **WHEN** code invokes a `Wait` overload with a timeout or cancellation token
- **THEN** EFD011 does not report because the first version is limited to unconditional parameterless blocking

#### Scenario: Nonblocking task use
- **WHEN** an eligible operation participates in `WhenAll`, continuation registration, return propagation, or another use without a recognized blocking consumer
- **THEN** EFD011 does not report

#### Scenario: Dynamic or unresolved call
- **WHEN** a candidate operation or blocking consumer is dynamic or unresolved
- **THEN** EFD011 does not report

### Requirement: Emit an actionable high-confidence EFD011 finding
Each matching blocking consumption SHALL produce an EFD011 diagnostic with title `Synchronous blocking on an EF Core async operation`, severity `Warning`, and confidence `High`. Evidence SHALL identify the resolved EF Core operation and blocking API. The finding SHALL explain thread blocking, thread-pool-starvation risk, and context-dependent deadlock risk without claiming a measured failure.

#### Scenario: Complete structured finding
- **WHEN** EFD011 reports a blocking consumption
- **THEN** console and JSON output contain every shared finding field with documentation key `EFD011`

#### Scenario: Exact diagnostic location
- **WHEN** EFD011 reports a blocking consumption
- **THEN** the finding range identifies the complete `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` expression

#### Scenario: Safe remediation guidance
- **WHEN** EFD011 reports a finding
- **THEN** remediation recommends awaiting the EF Core operation and propagating async through the caller, while acknowledging that truly synchronous boundaries may require deliberate isolation rather than another blocking wrapper

### Requirement: Honor standard suppression and existing reporting contracts
EFD011 SHALL use standard Roslyn diagnostic suppression, including pragma, editor configuration, and `SuppressMessage` where supported. Findings SHALL flow through existing local-only CLI, console and schema-versioned JSON renderers, deterministic ordering, de-duplication, privacy, and stable exit codes without a new command, schema version, network activity, telemetry, database execution, target-code execution, code fix, or proprietary suppression file.

#### Scenario: Standard suppression
- **WHEN** a matching expression is suppressed through a supported Roslyn mechanism with a recorded justification
- **THEN** the analyzer result consumed by the CLI excludes that finding

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan produces EFD011 findings
- **THEN** both formats contain the complete actionable finding and retain JSON schema version `1` and the successful-scan-with-findings exit code

### Requirement: Verify EFD011 with focused and end-to-end tests
The change SHALL include at least ten positive and ten negative EFD011 analyzer fixtures. Coverage SHALL include every supported blocking shape, extension and static EF query syntax, `DbContext` inheritance, `DbSet.FindAsync`, configuration of awaiters, multiple findings, unrelated and unresolved methods, ordinary awaits, stored tasks, controlled `Wait` overloads, comments, strings, inactive code, generated code, exact locations, and standard suppression. At least one end-to-end test SHALL invoke the CLI against a dedicated fixture and verify console and JSON output.

#### Scenario: Analyzer fixture suite
- **WHEN** the analyzer tests run
- **THEN** at least ten positive and ten negative cases verify the specified semantic matching, direct-expression boundary, diagnostic properties, location, and suppression

#### Scenario: CLI end-to-end fixture
- **WHEN** the CLI analyzes the EFD011 fixture project in console and JSON modes
- **THEN** both outputs contain only expected unsuppressed EFD011 findings with exact locations and JSON schema version `1`

#### Scenario: Full regression verification
- **WHEN** the solution is built and tested
- **THEN** EFD001 through EFD006, EFD011, and all CLI tests pass without warnings, errors, or analysis-time network requirements
