# Design

## Context

See `proposal.md` for motivation and `specs/efd005-unbounded-query-materialization/spec.md` for the revised behavior contract. EFD005 is implemented in `UnboundedQueryMaterializationAnalyzer` and reports whenever a proven inline EF query chain reaches a `ToList`/`ToListAsync` materializer without a `Queryable.Take` in the chain. Bound detection lives in `EfQueryOperationAnalysis.TryAnalyzeSource`, which records only `HasQueryableTake`. EFD004 (`PrematureQueryMaterializationAnalyzer`) already contains a conservative predicate/scalar/property-path model (`IsPredicateExpression`, `IsScalar`, `IsEntityPropertyPath`, `IsSimpleMappedProperty`) that is currently private to that analyzer.

A real-project run produced twelve findings, none actionable. Root cause: `Take` is the sole recognized bound, so every predicate-bounded query is treated as unbounded.

## Goals / Non-Goals

**Goals:**

- Recognize predicate-derived row bounds so EFD005 stops firing on the canonical `Contains`/key-equality hydration pattern.
- Guarantee that bound-neutral composition (`AsSplitQuery` and peers) never changes the EFD005 outcome, verified by a fixture that mirrors the production five-query shape.
- Downgrade findings in test projects instead of emitting warnings.
- Replace the self-contradicting remediation and the flat `Medium` confidence.
- Keep matching semantic, conservative, and linear in the inspected expression chain; keep EFD004 and EFD006 behavior unchanged; keep the JSON schema at version 1.

**Non-Goals:**

- General data-flow or interprocedural analysis. Tracing `ids` back to a prior materialized query is a bonus signal only where it is inline and cheap; it is not required for correctness.
- Runtime provider inference, cardinality estimation, or proving that a result is actually large.
- Automatic code fixes or predicate rewriting.
- Changing the JSON schema version, CLI syntax, or suppression mechanism.
- Any change to EFD006.

## Decisions

### 1. Reproduce the `AsSplitQuery` inconsistency before fixing it (W2)

Add a fixture to `EFD005.Sample` that mirrors the production method: five `Where(x => productIds.Contains(x.ProductId)).ToListAsync()` queries, some with `AsSplitQuery`, some composed across an intermediate `IQueryable` local, some with `Include`/`ThenInclude`. Establish the true differentiator before changing behavior.

The tracer treats `AsSplitQuery` as EF composition and passes through it, so it should not itself change the outcome. The leading hypothesis is that the unflagged queries are composed across statements (an `IQueryable` local or an `Include` chain the tracer stops at), which is a documented first-version limitation rather than a `Take`/`AsSplitQuery` interaction. The fix is chosen by what the fixture proves:

- If bound-neutral composition is genuinely changing the outcome, treat `AsSplitQuery`, `AsSingleQuery`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `Include`, `ThenInclude`, and `TagWith` as explicitly bound-neutral pass-through in `TryAnalyzeSourceCore` and assert identical outcomes with and without each.
- If cross-statement composition is the cause, make the limitation explicit and uniform: EFD005 analyzes only inline chains, and neither inline nor cross-statement form may depend on a bound-neutral token. Optionally trace a single unambiguous local assignment, deferred if it needs data flow.

Either way the contract is the same: **row-bound semantics decide the outcome; bound-neutral composition never does.**

The reproduction fixture confirmed the leading hypothesis. All inline variants were
reported by the pre-change analyzer regardless of `AsSplitQuery`, `Include`, or
`ThenInclude`; only the variant materialized through an intermediate `IQueryable`
local was not reported because origin tracing deliberately stops at a stored query
value. The neutral operators were therefore not suppressors. This change preserves
the inline-only boundary, makes the neutral pass-through set explicit, and tests
identical outcomes with and without each neutral operator.

Alternative considered: assume `AsSplitQuery` is the suppressor and special-case it. Rejected because the correlation is unproven and a wrong fix would mask the real mechanism.

### 2. Extract a shared bound model into `EfQueryOperationAnalysis` (W1)

Replace the boolean `HasQueryableTake` on `EfQueryChainAnalysis` with a `RowBound` describing kind and strength:

| Bound kind | Trigger | Strength | EFD005 effect |
|---|---|---|---|
| `Take` | resolved `Queryable.Take` in chain | Strong | suppress |
| `LocalCollectionMembership` | `Where(x => localCollection.Contains(x.Prop))` or `.Any(...)` over a local | Strong | suppress |
| `KeyEquality` | `Where(x => x.Key == value)` where the property is a PK/FK/alternate key | Strong | suppress |
| `KeyEqualityByName` | same shape, key proven only by `Id`/`*Id` name heuristic | Medium | downgrade |
| `TimeWindow` | `Where(x => x.DateTimeProp >= value)` or `<`/`<=`/`>` on a temporal column | Weak | downgrade |
| `None` | no recognized bound | — | report |

Predicate inspection reuses the EFD004 model. Move `IsPredicateExpression`, `IsScalar`, `IsEntityPropertyPath`, and `IsSimpleMappedProperty` from `PrematureQueryMaterializationAnalyzer` into `EfQueryOperationAnalysis` so both rules share one predicate model; EFD004 keeps identical behavior and its tests must still pass unchanged.

`Contains` membership is recognized when the receiver is a local, parameter, or field of an `ICollection<T>`/`IEnumerable<T>` type and the argument is an entity property path over the `Where` parameter. Key-ness is resolved from EF model metadata where statically available — `[Key]`, `[ForeignKey]`, and a unique single-column `[Index(IsUnique = true)]` alternate key (a non-unique index, or one column of a composite index, is not a cardinality bound); otherwise the `Id`/`<Entity>Id`/`*Id` name heuristic assigns the lower-strength `KeyEqualityByName`. The traversal remains linear and inspects only `Where`-lambda predicates already reachable in the proven chain — it does not add general data-flow. See Decision 7 for the conjunction-decomposition and null-guard refinements added during revalidation.

Alternative considered: keep EFD004's predicate code private and duplicate it. Rejected because two divergent predicate models would drift and double the test burden.

### 3. Redefine "unbounded" as "no strong bound and no client boundary" (W1)

EFD005 reports only when the proven chain has bound kind `None`, or a `Weak`/`Medium` bound that downgrades rather than suppresses. Any `Strong` bound suppresses. The existing exclusions are unchanged: `AsEnumerable`/`AsAsyncEnumerable` client boundaries, arbitrary `IQueryable` sources, cross-statement composition (first-version limitation), and EFD004 overlap.

Alternative considered: keep reporting weak bounds at full confidence. Rejected because a time-window predicate is a strong intent signal even when it is a weak numeric bound.

### 4. Downgrade in test projects — universal, not per-rule (W3)

The test-project downgrade is rule-agnostic and lives in the CLI analysis pipeline, not in any analyzer. `WorkspaceAnalyzer` already holds the Roslyn `Project`/`Compilation` per project and maps diagnostics into `Finding`s; add one classification-and-downgrade step there so it covers EFD001–EFD011 and every future rule automatically, with no per-analyzer code.

Classify a project as a test project when its compilation references a recognized test framework (`xunit`, `nunit.framework`, `Microsoft.VisualStudio.TestPlatform.*`, `Microsoft.NET.Test.Sdk`) or its MSBuild `IsTestProject` property is set. For findings from such a project, rewrite the mapped immutable `Finding` with `Advisory` confidence, which then flows through the confidence-driven severity mapping (Decision 5) to `Info`. Downgrade, not silent-skip, so a genuine issue in a test helper stays discoverable and tunable through standard severity configuration.

Because analyzers report their own rule-assigned confidence and the downgrade is applied centrally afterward, individual rules — including EFD005's blast-radius gradient — never special-case test context. Adding a rule later inherits the behavior for free.

Alternative considered: detect the test context inside each analyzer's compilation-start action. Rejected because it duplicates detection across every present and future rule and couples rule logic to project classification. Alternative considered: skip test projects entirely. Rejected because a genuinely problematic query in a test helper should still be discoverable at `Info`.

### 5. Confidence gradient and confidence-driven severity (W5)

Replace the constant `Confidence = "medium"` with a computed level:

- **High** — bound kind `None`, entity is not a known small lookup, and the call site is a hot path (a handler/controller/service method rather than a migration, seed, or `Main`).
- **Medium** — a `Medium`-strength bound (`KeyEqualityByName`), or `None` without the hot-path/entity signals.
- **Advisory** — a `Weak` bound (`TimeWindow`).

Hot-path and small-lookup signals are best-effort heuristics from the enclosing symbol and entity name; when unknown they default to Medium so the rule never over-claims. The universal test-project downgrade (Decision 4) is applied centrally after this rule-assigned confidence, so EFD005 does not itself special-case test context. A Roslyn `DiagnosticDescriptor` has one fixed severity, so severity is derived downstream: `DiagnosticFindingMapper` maps the confidence property to `FindingSeverity` (`Advisory` → `Info`, otherwise `Warning`). This is the only reporting-layer change and keeps analyzers declarative. The finding shape and JSON schema version 1 are unchanged.

Alternative considered: emit multiple `DiagnosticDescriptor`s per severity. Rejected because it multiplies rule identity and complicates suppression; a single ID with confidence-driven severity is simpler and preserves `dotnet_diagnostic.EFD005.severity` configuration.

### 6. Rewrite remediation and impact wording (W4)

Lead with intent, not `Take`:

> If this result set can grow without bound and you do not need every row, consider server-side paging with stable ordering, chunked processing, or a purpose-built aggregate. If you need every matching row — for example hydrating children for a known set of parents — full materialization is expected; suppress with a recorded reason.

Remove `Take` as the default suggestion and remove the contradictory "do not add an arbitrary limit" clause, folding that caution into the intent framing. Impact language stays qualified and never claims a measured or large result.

### 7. Predicate-bound recognition refinements from real-project revalidation (W1)

Task 10.1 re-ran EFD005 against the originating corpus and exposed three gaps in the
first bound-recognition pass. All three are refinements to how the row bound is read
from an already-proven inline chain; none change the chain model or exclusions.

- **Conjunction decomposition.** Bound recognition originally classified only the whole
  `Where` lambda body, so `Where(x => ids.Contains(x.Fk) && x.Other == v)` and
  `Where(x => x.Flag && ids.Contains(x.Fk))` fell through to `None`. A conjunction can
  only narrow a result set, so `ClassifyPredicateBound` now recurses through top-level
  `&&` operands and takes the strongest bound among the conjuncts. Disjunctions (`||`)
  are deliberately not decomposed: an unbounded branch keeps the whole predicate
  unbounded. This is what makes the canonical `ids.Contains(x.Fk) && <extra filters>`
  hydration shape suppress.
- **Null-guarded key paths.** `nullable.Value` and `nullable.GetValueOrDefault()` are
  now transparent when resolving an entity property path, so the common
  `x => x.OptionalFk.HasValue && ids.Contains(x.OptionalFk.Value)` shape resolves to the
  entity's `OptionalFk` property rather than the `Nullable<T>` member.
- **Unique, single-column index only.** `KeyEquality`/`Strong` from index metadata is now
  restricted to a unique, single-column index — the only index shape that is a true
  alternate key that bounds a single-property equality to one row. Membership of a
  non-unique index, or of one column of a composite index, no longer counts as a strong
  bound (it falls to the `Id`/`*Id` name heuristic or `None`). This corrected a false
  negative: a `Where(x => x.EnumCol == v && x.When >= t)` time-window scan over an entity
  whose composite `[Index]` merely happened to include `EnumCol` was being suppressed as
  key-equality; it now reports at `TimeWindow`/advisory as intended.

Outcome on the corpus: the production false positives (`Contains` inside a
compound predicate, and a null-guarded `Contains(x.Fk.Value)`) are gone, no new findings
appear, and the genuine time-window scan continues to report.

## Risks / Trade-offs

- **[Key-ness is not always statically provable]** → Use EF metadata where available and a lower-strength name heuristic otherwise; a `KeyEqualityByName` bound downgrades rather than suppresses, so a wrong guess yields an advisory finding, not a missed strong bound.
- **[Sharing the predicate model could regress EFD004]** → Extract without behavior change and keep all existing EFD004 fixtures green as a gate.
- **[Test-framework detection could misclassify a production project that references a test SDK]** → Downgrade to `Info` rather than skip, so a misclassification loses severity but not discoverability.
- **[Confidence-driven severity changes finding-reporting output]** → Only `Advisory` maps to `Info`; `Medium`/`High` remain `Warning`, so existing warning-based consumers and exit codes are unaffected; assert with reporting tests.
- **[Downgrading in test projects could hide a real unbounded test query]** → Accepted; `Info` keeps it discoverable and configurable.
- **[The `AsSplitQuery` fix could over-generalize pass-through]** → Enumerate bound-neutral methods explicitly and assert outcome parity with and without each.

## Migration Plan

1. Add the reproduction fixture for the production five-query shape and pin current behavior (W2 investigation).
2. Extract the shared predicate model into `EfQueryOperationAnalysis` with EFD004 regression tests green.
3. Add the `RowBound` model (kind + strength) and predicate-bound recognition; update EFD005 to consume it (W1).
4. Enumerate bound-neutral composition as outcome-neutral and assert parity (W2 fix).
5. Add test-project detection and downgrade (W3).
6. Add the confidence gradient and confidence-driven severity in the mapper (W5).
7. Rewrite remediation/impact text (W4).
8. Update `docs/rules/EFD005.md` and README; re-validate against the real-project corpus and the 90% precision gate.

No persisted data or report migration is required. Rollback restores the `HasQueryableTake`-only bound model and the flat `Medium` confidence; schema-version-1 consumers remain compatible.
