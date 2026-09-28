# Proposal

## Why

EFDoctor needs a trustworthy first vertical slice that proves its local-first analyzer and CLI architecture before the rule catalog expands. EFD001 is a high-confidence starting point because semantic identification of EF Core saves inside loops can provide precise, explainable evidence with controlled false-positive risk.

## What Changes

- Bootstrap a .NET solution with separate projects for Roslyn analyzers, shared finding/reporting contracts, CLI orchestration, analyzer tests, CLI integration tests, and fixture projects.
- Add EFD001 semantic analysis for EF Core `DbContext.SaveChanges` and `SaveChangesAsync` invocations executed within `for`, `foreach`, `await foreach`, `while`, and `do`/`while` loops.
- Support inherited and overridden EF Core save methods while excluding unresolved symbols, unrelated same-name methods, calls outside loops, and deferred lambda, anonymous-function, or local-function bodies declared within loops.
- Define a structured finding contract containing identity, severity, confidence, source span, evidence, impact, remediation, and documentation reference fields.
- Add a local-only CLI command that accepts a solution or project path and emits deterministic human-readable or schema-versioned JSON results, with automation-friendly output and stable exit codes.
- Use standard Roslyn suppression mechanisms and document how intentional EFD001 findings can be suppressed with a justification.
- Add at least ten positive and ten negative analyzer fixtures plus an end-to-end CLI test covering console and JSON output.
- Document clean-checkout build, test, local execution, output, exit-code, limitation, and suppression guidance.

## Capabilities

### New Capabilities

- `efd001-detection`: Semantically identify EF Core save operations that execute as part of supported loop bodies, produce one high-confidence diagnostic per invocation, and honor standard Roslyn suppression.
- `finding-reporting`: Represent actionable findings consistently and render deterministically ordered console and versioned JSON reports.
- `cli-analysis`: Validate a supplied .NET solution or project path, analyze it entirely on the local machine, select output behavior suitable for people or automation, and return stable exit codes.

### Modified Capabilities

None.

## Impact

- Introduces the initial solution and project layout under `src/` and `tests/`, plus fixture/sample inputs and concise developer documentation.
- Adds dependencies on the .NET 10 SDK for CLI/test projects, analyzer-compatible Roslyn APIs for the analyzer project, EF Core reference assemblies for semantic identification, and MSBuild workspace support for loading projects and solutions.
- Establishes EFD001 and the first externally consumable finding/JSON contracts; future rule changes must preserve deterministic reporting and evolve the JSON schema explicitly.
- Performs no network calls, source upload, telemetry, account, licensing, publication, IDE-extension, live-database, or CI/CD work.
