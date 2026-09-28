# Proposal

## Why

Blocking on EF Core asynchronous database operations with `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` can tie up request threads and introduce deadlock or thread-pool-starvation risk. EFD011 is the project brief's next recommended rule because semantic matching can detect this high-impact pattern with low false-positive risk.

## What Changes

- Add EFD011 semantic analysis for synchronous blocking consumption of resolved EF Core asynchronous operations.
- Cover `.Result`, parameterless `.Wait()`, and `.GetAwaiter().GetResult()` while excluding awaited operations, unrelated tasks, non-EF methods, and timeout/cancellation `Wait` overloads.
- Emit high-confidence, actionable findings through the existing console and JSON contracts and standard Roslyn suppression.
- Add focused analyzer fixtures, a dedicated CLI fixture, end-to-end coverage, release metadata, and rule documentation.

## Capabilities

### New Capabilities

- `efd011-sync-over-async`: Define semantic detection and reporting for blocking consumption of EF Core asynchronous database operations.

### Modified Capabilities

- `cli-analysis`: Register EFD011 in the existing local workspace analysis pipeline.

## Impact

The change affects the analyzer assembly, CLI analyzer registration, analyzer and CLI tests, test fixtures, analyzer release metadata, the README, and rule documentation. It adds no command, output-schema, package, network, telemetry, database execution, target-code execution, code-fix, or proprietary suppression changes.
