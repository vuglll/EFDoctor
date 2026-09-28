# Proposal

## Why

A real-project run produced twelve EFD005 findings and zero were actionable. The rule equates "no query-side `Take`" with "unbounded," but a row bound most often comes from the predicate, not from `Take`. Every finding was on a bounded `Where(x => ids.Contains(x.Fk))` or key-equality query — the canonical, correct "hydrate children for a known set of parents" pattern — so the rule fired on the most common legitimate EF query shape in existence.

The same run exposed four secondary problems: EFD005 silently produced different outcomes for row-bound-equivalent queries that differed only by bound-neutral composition (for example `AsSplitQuery`); most findings were in integration-test projects where full materialization over known-small fixtures is never the target; the remediation text is self-contradicting (it recommends `Take` and then warns against arbitrary limits); and every finding carries a flat `Medium` confidence, which developers read as background noise. A linter that emits only false positives is suppressed wholesale within a week and then never catches the real regression.

This change raises the bar for when EFD005 fires. It does not broaden the product beyond local static analysis and does not change EFD006, which the same run confirmed is trustworthy.

## What Changes

- **W1 — Recognize predicate-derived row bounds.** Extend the shared query-chain analysis so a bound can be established by the predicate, not only by `Queryable.Take`: `Contains`/`Any` over a local collection, equality on a key column (PK/FK/alternate key, with a lower-strength name heuristic fallback), and already-bounded or non-materializing terminals. Strong bounds suppress the finding; weak bounds (for example a time-window comparison) downgrade confidence rather than suppress.
- **W2 — Make bound-neutral composition outcome-neutral.** `AsSplitQuery`, `AsSingleQuery`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `Include`, `ThenInclude`, `TagWith`, and similar shaping MUST NOT change whether EFD005 fires. A fixture reproduces the exact five-query production shape first, so the true differentiator is fixed rather than guessed.
- **W3 — Downgrade findings in test projects (universal).** Add a single, rule-agnostic step in the CLI analysis pipeline: when the analyzed project references a recognized test framework (xUnit, NUnit, MSTest) or is marked `IsTestProject`, every EFDoctor finding it produces — from any rule, present or future — is downgraded to advisory confidence and `Info` severity rather than emitted as a warning. This is not specific to EFD005; it applies uniformly to EFD001–EFD011 and all later rules without per-rule code.
- **W4 — Rewrite the remediation.** Replace the self-contradicting `Take`-first text with question-first guidance that leads with intent ("if this set can grow without bound and you do not need every row…"), acknowledges the legitimate hydrate-children case, and removes `Take` as the default suggestion.
- **W5 — Assign confidence by blast radius.** Replace the flat `Medium` with a computed level from bound strength, entity shape, and call-site context, and drive finding severity from confidence in the CLI mapper so advisory findings render as `Info`.

## Capabilities

### Modified Capabilities

- `efd005-unbounded-query-materialization`: Redefines the row-bound model to include predicate-derived bounds, requires bound-neutral composition to be outcome-neutral, replaces the remediation guidance, and replaces flat confidence with a blast-radius gradient.
- `cli-analysis`: Adds a universal, rule-agnostic test-project downgrade so any finding from any rule (present or future) that originates in a test project is reported at advisory confidence and `Info` severity.
- `finding-reporting`: Derives finding severity from confidence so advisory findings render as `Info` in console and schema-version-1 JSON without a schema-version change.

### New Capabilities

None. This change refines existing capabilities and reuses the current analyzer, finding, suppression, CLI, and reporting contracts.

## Impact

- Extends `EfQueryOperationAnalysis` with a shared predicate/bound model reused by EFD004 and EFD005, and updates `UnboundedQueryMaterializationAnalyzer` to consume it.
- Updates `DiagnosticFindingMapper` to derive `FindingSeverity` from the reported confidence property; no JSON schema-version change.
- Adds central test-project classification and a rule-agnostic finding downgrade to `WorkspaceAnalyzer`, applied to every EFDoctor rule.
- Extends analyzer fixtures with at least ten positive and ten negative EFD005 cases covering each bound kind, the five-query production shape, bound-neutral composition, and test-project downgrade; extends CLI end-to-end coverage.
- Updates `docs/rules/EFD005.md` and README EFD005 guidance.
- Re-validates against the originating real-project corpus: the three production false positives MUST be gone and precision MUST be at least 90%.
- Introduces no network calls, telemetry, code fixes, database connectivity, or new CLI command surface. EFD006 behavior is unchanged.
