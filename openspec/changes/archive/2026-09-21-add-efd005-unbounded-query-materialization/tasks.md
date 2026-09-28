# Tasks

## 1. Shared Semantic Foundations

- [x] 1.1 Extract narrow operation helpers for method normalization, transparent unwrapping, invocation-source access, and inline EF query-origin tracing from EFD004 without changing its behavior; verify all focused EFD004 analyzer tests still pass.
- [x] 1.2 Extend the shared query-chain result to record deterministic resolved operation names and the presence of `System.Linq.Queryable.Take`; verify direct `DbSet`, `DbContext.Set<TEntity>()`, composed-query, client-boundary, and arbitrary-`IQueryable` origin tests.
- [x] 1.3 Expose the existing EFD004 immediate-consumer eligibility decision for shared overlap handling; verify eligible EFD004 shapes suppress EFD005 while unsupported client expressions remain EFD005 candidates.

## 2. EFD005 Analyzer

- [x] 2.1 Implement semantic recognition of `Enumerable.ToList` and EF Core `ToListAsync` in extension and static syntax, excluding `ToArray`, unrelated, unresolved, in-memory, and generated-code cases; verify focused materializer tests pass.
- [x] 2.2 Implement unbounded-chain classification so `Where`, projection, ordering, `Distinct`, `Skip`, `Include`, and tracking modifiers remain reportable while any resolved query-side `Take` prevents EFD005; verify constant, parameterized, and composed-bound tests pass.
- [x] 2.3 Implement the EFD005 descriptor and diagnostic properties with warning severity, medium confidence, exact invocation span, deterministic chain evidence, qualified impact, semantics-preserving remediation, legitimate exceptions, and documentation key; verify descriptor and finding-contract assertions pass.
- [x] 2.4 Add EFD005 to the unshipped analyzer release notes and verify the analyzer project builds with no new production dependency.

## 3. Analyzer Test Coverage

- [x] 3.1 Add at least ten positive fixtures covering direct and composed EF sources, sync and async calls, static syntax, filters, projection, ordering, `Distinct`, `Skip`, `Include`, tracking modifiers, non-awaited task flow, and multiple findings; verify counts and exact locations.
- [x] 3.2 Add at least ten negative fixtures covering constant and parameterized `Take`, composition after `Take`, in-memory sources, arbitrary and stored queries, client boundaries, unrelated and unresolved methods, `ToArray`, generated code, comments/strings/inactive code, and EFD004 overlap; verify no false positives or duplicate locations.
- [x] 3.3 Add pragma, editor-configuration, and supported `SuppressMessage` tests with recorded justifications; verify suppressed findings are absent and unsuppressed findings retain complete actionable properties.

## 4. CLI Integration and Documentation

- [x] 4.1 Register EFD005 in the CLI analyzer set and create a dedicated compilable EFD005 fixture project with reported, bounded, overlap, and all three suppression cases; verify the fixture builds without changing existing fixture counts.
- [x] 4.2 Add end-to-end console and JSON tests for the EFD005 fixture; verify exact ranges, medium confidence, evidence, deterministic ordering, schema version `1`, suppression, no EFD004/EFD005 duplicate, and successful-scan-with-findings exit code.
- [x] 4.3 Add `docs/rules/EFD005.md` and update README status, the stale EFD005 catalog entry, semantic and expression-local boundaries, bound definition, remediation, legitimate exceptions, suppression, and verification maps; verify documented paths and commands are valid.

## 5. Final Verification

- [x] 5.1 Restore dependencies, clean and build the entire solution, run all automated tests, run strict OpenSpec validation, and inspect production changes to verify EFD001-EFD005 pass with no warnings/errors and EFD005 adds no network, telemetry, database execution, target-code execution, code fix, arbitrary row-limit rewrite, or proprietary suppression behavior.
