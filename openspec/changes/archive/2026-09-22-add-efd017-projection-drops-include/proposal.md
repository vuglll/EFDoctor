# Proposal

## Why

An EF Core `Include` placed before a projection can be discarded when the projection no longer returns the entity for which the navigation was requested, leaving misleading query intent and sometimes unexpectedly unpopulated navigation data. EFD017 is the product brief's next recommended rule after the completed EFD011-EFD013 sequence because this high-value correctness issue has a semantically precise, low-noise static shape.

## What Changes

- Add EFD017 detection for semantically proven EF Core `Include`/`ThenInclude` chains followed inline by a LINQ `Select` projection that does not preserve an entity on which the include can take effect.
- Restrict the initial rule to query chains traced to `DbSet<T>` or `DbContext.Set<T>()`, resolved EF Core include methods, and resolved `Queryable.Select`; do not infer through locals, helpers, custom query operators, or ambiguous expression flow.
- Avoid reports when no include precedes the projection, the selector preserves the source entity in its result, the chain is no longer a proven EF query, or method names merely resemble EF/LINQ APIs.
- Emit a high-confidence correctness warning at the projection boundary with the include path(s), projection shape, and query origin as evidence. Remediation will distinguish removing a redundant include and explicitly projecting required data from returning the entity when populated navigations are genuinely required.
- Register EFD017 in CLI workspace analysis and add analyzer, fixture, suppression, console, JSON, documentation, release-metadata, and regression coverage without changing the report schema.

## Capabilities

### New Capabilities

- `efd017-projection-drops-include`: Defines conservative semantic detection, exclusions, evidence, remediation, suppression, and validation coverage for includes rendered ineffective by a later projection.

### Modified Capabilities

- `cli-analysis`: Requires EFD017 to run in CLI analysis and participate in the existing reporting, ordering, suppression, privacy, test-project downgrade, and exit-code contracts.

## Impact

The change adds one Roslyn analyzer, limited shared query-chain support where useful, analyzer and CLI fixtures/tests, EFD017 rule documentation, CLI analyzer registration, release metadata, and README status updates. It reuses the current Roslyn and EF Core test dependencies, remains provider-agnostic and local-only, adds no code fix, and does not change the versioned JSON schema.
