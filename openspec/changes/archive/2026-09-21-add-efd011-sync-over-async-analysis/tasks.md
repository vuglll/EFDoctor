# Tasks

## 1. EFD011 Semantic Analyzer

- [x] 1.1 Implement semantic recognition of supported EF Core async query terminals, `DbContext.SaveChangesAsync` overrides, and `DbSet.FindAsync`; verify focused source-family and unrelated-method tests pass.
- [x] 1.2 Implement consumer-first recognition for framework `Task`/`ValueTask` `.Result`, parameterless `Task.Wait()`, and task/value-task awaiter `GetResult()`, including `ConfigureAwait`; verify exact direct-chain and overload-boundary tests pass.
- [x] 1.3 Emit one EFD011 diagnostic per blocking consumption with warning severity, high confidence, exact complete-expression location, deterministic semantic evidence, qualified impact, safe remediation, and documentation key; verify diagnostic property and location assertions pass.
- [x] 1.4 Add EFD011 to unshipped analyzer release metadata and verify the analyzer project builds without a new production dependency.

## 2. Analyzer Test Coverage

- [x] 2.1 Add at least ten positive cases covering result, wait, awaiter, configured awaiter, query-terminal extension/static syntax, context inheritance, find, multiple operations, and parentheses; verify counts and exact locations.
- [x] 2.2 Add at least ten negative cases covering proper await, stored tasks, controlled waits, arbitrary tasks, unrelated and unresolved methods, nonblocking composition, comments, strings, inactive code, and generated code; verify no false positives.
- [x] 2.3 Add pragma, editor-configuration, and supported `SuppressMessage` tests with recorded justifications; verify suppressed findings are absent and unsuppressed findings retain complete actionable properties.

## 3. CLI Integration and Documentation

- [x] 3.1 Register EFD011 in the CLI analyzer set and create a dedicated compilable EFD011 fixture with unsuppressed, awaited, and pragma/editor-config/attribute-suppressed cases; verify the fixture builds.
- [x] 3.2 Add end-to-end console and JSON tests for the EFD011 fixture; verify exact ranges, high confidence, evidence, deterministic ordering, schema version `1`, suppression, and successful-scan-with-findings exit code.
- [x] 3.3 Add `docs/rules/EFD011.md` and update README status, semantic boundary, remediation trade-offs, suppression, and verification maps; verify documented paths and commands are valid.

## 4. Final Verification

- [x] 4.1 Restore dependencies, build the solution, run all automated tests, run strict OpenSpec validation, and inspect production changes to verify EFD001-EFD006 and EFD011 pass with no warnings/errors and EFD011 adds no network, telemetry, database execution, target-code execution, code fix, output-schema change, or proprietary suppression behavior.
