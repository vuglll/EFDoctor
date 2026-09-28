# Proposal

## Why

EF Core merges every `Include` path in a query, so repeating a path, or including a navigation that a longer path in the same query already loads, has no effect. These leftovers accumulate as queries are edited and make readers believe the loading shape is more complex than it is. EFD025 is the product brief's cheap, very-low-false-positive cleanup rule. It reuses the include-chain machinery that EFD006 and EFD017 already built, so it costs little to add now.

## What Changes

- Add EFD025 detection for include paths in one proven EF Core query chain that are exact duplicates of another path, or strict prefixes of a longer path, in the same chain. Paths are built from semantically resolved `Include`/`ThenInclude` calls.
- Support expression-based include paths (including nested member paths such as `o => o.Customer.Address` and `ThenInclude` continuations) and constant string include paths. String paths are compared only with other string paths.
- Leave the repeated `Include(x => x.Nav)` alone when it is the required syntax for branching into a different `ThenInclude`. For example, `.Include(b => b.Posts).ThenInclude(p => p.Author).Include(b => b.Posts).ThenInclude(p => p.Tags)` does not report.
- Stay silent on filtered includes, casts to derived types, unrecognized selector shapes, auto-includes declared in the model, and include state that flows through locals, helpers, or element-type-changing operators.
- Emit one high-confidence, `Info`-severity maintainability finding for each redundant include chain. Evidence names the redundant path and the path that already covers it. The remediation is to delete the redundant call.
- Register EFD025 in CLI workspace analysis and add analyzer, fixture, suppression, console, JSON, documentation, release-metadata, and README coverage. The report schema does not change.

## Capabilities

### New Capabilities

- `efd025-redundant-include`: Defines conservative semantic detection, exclusions, evidence, remediation, suppression, and validation coverage for include paths that are duplicated or already covered by a longer path in the same query.

### Modified Capabilities

- `cli-analysis`: EFD025 must run in CLI analysis and follow the existing reporting, ordering, suppression, privacy, test-project, and exit-code contracts.

## Impact

The change adds one Roslyn analyzer (`RedundantIncludeAnalyzer`) and, where it avoids duplication, a small shared include-path helper extracted from `ProjectionDropsIncludeAnalyzer`. It also adds analyzer tests, an `EFD025.Sample` CLI fixture, a case in the shared test-project fixture, `docs/rules/EFD025.md`, the CLI analyzer registration, an `AnalyzerReleases.Unshipped.md` entry, and README and CHANGELOG updates. It stays provider-agnostic and local-only, adds no dependency or code fix, and does not change the versioned JSON schema.

EFD025 uses `Info` severity. Because any finding produces exit code `1`, an unsuppressed EFD025 finding still makes a scan return `1`.
