# Design

## Context

EFDoctor source analyzers register compilation-scoped Roslyn operation actions, resolve EF APIs by symbols, attach a shared set of diagnostic properties, and rely on the CLI for common mapping and test-project downgrades. Relational raw-SQL APIs come from the EF Core relational assembly, which is already available to the solution's test and CLI projects. See `proposal.md` for motivation and the EFD012 and CLI deltas for behavior.

## Goals / Non-Goals

**Goals:**

- Prove both the EF Core raw API and the unsafe SQL construction before reporting.
- Cover direct interpolation/concatenation and conservative same-body local origins.
- Reuse existing finding, suppression, CLI, and fixture conventions.

**Non-Goals:**

- General taint analysis across fields, properties, parameters, methods, or projects.
- Judging whether a dynamic fragment was validated at runtime.
- Parsing SQL, adding a code fix, or covering non-EF database libraries.

## Decisions

### Resolve the raw API from normalized method symbols

At compilation start, resolve EF Core's relational extension types and register an invocation operation action only when those symbols exist. Normalize reduced extension methods and require the expected containing type, method name, and string SQL parameter. This supports reduced and static forms while rejecting application methods with matching names. Name-only matching was rejected because it violates the project's low-false-positive standard.

### Classify the SQL argument by operation shape

Treat `IInterpolatedStringOperation` as unsafe. Treat string `IBinaryOperation` addition as unsafe only when the complete expression lacks a compile-time constant value and at least one operand is non-constant. Unwrap implicit conversions and parentheses before classification. Constant literals and constant-folded concatenations remain clean; the presence of separate value arguments does not make dynamic SQL text safe.

### Use conservative local reaching-definition analysis

For a local reference used as SQL, inspect the containing operation tree for writes to that same local that lexically precede the call. Report only when exactly one reaching write is available and its assigned value recursively classifies as unsafe. If there are multiple writes, conditional/control-flow ambiguity, an uninitialized declaration, a ref/out escape, or an unsupported origin, return unknown and do not report. A bounded visited-local set prevents cycles. Full interprocedural taint analysis was rejected as disproportionate and likely to create uncertain findings.

### Report the SQL argument at the call site

Locate the diagnostic on the actual SQL argument expression, including a local identifier when local-origin analysis is used. Evidence names the resolved EF method and whether the origin was interpolation or concatenation. This keeps the action point precise while evidence explains the upstream proof.

### Keep integration centralized

Add the analyzer once to `WorkspaceAnalyzer`; existing mapping, rendering, suppression, ordering, exit codes, and the shared test-project downgrade remain unchanged. Add a focused buildable fixture so process-level console and JSON assertions validate the complete path.

## Risks / Trade-offs

- [Conservative local analysis misses complex but unsafe flows] → Prefer silence unless the origin is uniquely proven; document the limitation and cover only deterministic same-body assignments.
- [Roslyn represents interpolated strings through conversions or handlers] → Test the operation shapes emitted by the repository's supported compiler and unwrap conversions before classification.
- [EF Core overloads evolve] → Match resolved extension owners plus the SQL parameter rather than a single full signature, and pin supported calls in tests.
- [Security wording overstates certainty] → State that the construct can enable injection and plan-cache churn; do not claim exploitability or measured impact.

## Migration Plan

Ship EFD012 enabled by default with the other analyzers, update release metadata and documentation, and validate the entire suite. Rollback consists of removing the analyzer registration and EFD012 implementation/tests/docs; no persisted data or public JSON schema migration is involved.
