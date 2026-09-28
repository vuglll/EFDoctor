# Tasks

## 1. Semantic Analysis Foundations

- [x] 1.1 Add EFD004 test-source helpers for EF `DbSet` query composition and asynchronous methods, and verify representative sync and async snippets compile without C# errors.
- [x] 1.2 Implement an expression-local EF query-source classifier that traces resolved `Queryable` and EF composition back to `DbSet` while rejecting arbitrary `IQueryable` and explicit client boundaries; verify focused source-origin tests pass.
- [x] 1.3 Implement supported materializer recognition for resolved `Enumerable.ToList`/`ToArray` and EF Core `ToListAsync`/`ToArrayAsync`, including static and extension syntax; verify unrelated and unresolved same-named methods are rejected.

## 2. Downstream Composition Classification

- [x] 2.1 Implement transparent parent traversal through parentheses, conversions, and one `await` to identify only the immediate outer `Enumerable` consumer; verify unsupported intervening operations and stored-result uses do not match.
- [x] 2.2 Implement conservative `Where` argument classification for direct property paths, scalar captured values, null checks, built-in comparisons, and boolean combinations; verify custom methods, indexed predicates, dynamic expressions, assignments, and user-defined operators are rejected.
- [x] 2.3 Implement conservative `Select`, `OrderBy`/`OrderByDescending`, and `Skip`/`Take` classifiers; verify supported property/anonymous projections, direct ordering keys, and stable integral paging values plus their negative boundaries.

## 3. EFD004 Diagnostic

- [x] 3.1 Implement `PrematureQueryMaterializationAnalyzer` with warning severity, high confidence, one diagnostic per matching materializer, exact invocation location, operator-specific impact, qualified remediation, and all shared diagnostic properties; verify focused descriptor and finding-contract tests pass.
- [x] 3.2 Add EFD004 to the unshipped analyzer release notes and verify the analyzer project builds with release tracking and no new production dependency.

## 4. Analyzer Test Coverage

- [x] 4.1 Add at least ten positive EFD004 fixtures covering all four materializers, filtering, projection, both ordering directions, paging, composed EF sources, async awaiting, static syntax, multiple downstream operations, and multiple findings; verify expected counts and exact locations.
- [x] 4.2 Add at least ten negative EFD004 fixtures covering terminal materialization, pre-materialization composition, in-memory and arbitrary-queryable sources, explicit client boundaries, stored locals, custom methods/computed members, indexed lambdas, user-defined operators, unrelated/unresolved calls, and comments/strings/inactive code; verify no false positives.
- [x] 4.3 Add pragma, editor-configuration, and supported `SuppressMessage` tests plus actionable evidence/remediation assertions; verify suppressed findings are absent and unsuppressed findings remain complete.

## 5. CLI Integration and Documentation

- [x] 5.1 Register EFD004 in the CLI analyzer set, create a dedicated compilable EFD004 fixture project with reported and suppressed cases, and verify the fixture builds without changing existing fixture counts.
- [x] 5.2 Add end-to-end console and JSON tests for the EFD004 fixture, and verify exact source ranges, evidence, deterministic ordering, schema version `1`, suppression, and successful-scan-with-findings exit code.
- [x] 5.3 Add `docs/rules/EFD004.md` and update README status, semantic/expression boundaries, remediation, legitimate exceptions, suppression, and verification maps; verify all documented paths and commands are valid.

## 6. Final Verification

- [x] 6.1 Restore dependencies, clean and build the entire solution, run all automated tests, run strict OpenSpec validation, and inspect production code to verify EFD001-EFD004 pass with no warnings/errors and EFD004 adds no network, telemetry, database execution, target-code execution, code fix, or proprietary suppression behavior.
