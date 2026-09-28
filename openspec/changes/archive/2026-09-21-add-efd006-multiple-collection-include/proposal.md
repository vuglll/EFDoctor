# Proposal

## Why

Loading multiple sibling collection navigations in one EF Core query can multiply result rows and transfer large amounts of duplicated data. EFD006 is the next unimplemented rule in the product brief's immediate query-shape sequence and adds a useful, medium-confidence warning while keeping detection semantic and explainable.

## What Changes

- Add EFD006 detection for proven EF Core query chains containing multiple distinct sibling collection `Include` paths.
- Avoid findings for reference navigations, nested `ThenInclude` paths, duplicate paths, split queries, arbitrary queryables, unresolved calls, and explicit client boundaries.
- Emit actionable evidence, impact, remediation, confidence, and documentation through the existing console and JSON reporting contracts.
- Add focused analyzer fixtures, a dedicated CLI fixture, end-to-end coverage, release metadata, and rule documentation.

## Capabilities

### New Capabilities

- `efd006-multiple-collection-include`: Define semantic detection and reporting for multiple sibling collection includes that can create a single-query cartesian product.

### Modified Capabilities

- `cli-analysis`: Register EFD006 in the existing local workspace analysis pipeline.

## Impact

The change affects the analyzer assembly, shared EF query-chain analysis where useful, CLI analyzer registration, analyzer and CLI tests, test fixtures, analyzer release metadata, the README, and rule documentation. It adds no command, output-schema, package, network, telemetry, or proprietary suppression changes.
