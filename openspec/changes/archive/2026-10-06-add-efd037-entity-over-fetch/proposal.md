# Proposal

## Why

A query that materializes full entities when the code reads two of their twenty columns fetches, transfers, and tracks far more than it needs. It was suggested on the 0.3.0 announcement thread (issue #10), and no shipped rule covers it: EFD004 reports only an inline `ToList().Select(…)`, not a result held in a local and read by a loop or member access.

## What Changes

- Add EFD037, an advisory rule that reports an EF Core query materialized into a local when the method reads only a small subset of the entity's mapped scalar properties, and suggests projecting with `Select` in the query.
- The rule follows the materialized result, not the query: every use of the local, and of each element taken from it, must be a read of a scalar property. Any other use means the entity may be needed whole, and the rule stays silent.
- A finding needs at most half of the entity's scalar properties read, and at least four left unused.
- Ship it in both the CLI and the analyzer package, with a rule page, fixtures, and end-to-end coverage.

## Capabilities

### New Capabilities

- `efd037-entity-over-fetch`: detection and reporting of entities materialized into a local of which only a few mapped scalar properties are read.

### Modified Capabilities

None.

## Impact

Adds one analyzer, its tests, a sample fixture, a case in the shared test-project fixture, and its documentation entries. No existing rule, the finding contract, the JSON schema, or exit codes change.
