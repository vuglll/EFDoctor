# Tasks

## 1. Reproduce and pin the AsSplitQuery inconsistency (W2)

- [x] 1.1 Add an `EFD005.Sample` fixture mirroring the production method: five `Where(x => productIds.Contains(x.ProductId)).ToListAsync()` queries, variants with `AsSplitQuery`, an intermediate `IQueryable` local, and `Include`/`ThenInclude`; run the current analyzer and record which variants report today.
- [x] 1.2 Determine and document the true differentiator (inline vs. cross-statement composition vs. a genuine bound-neutral interaction), and capture the finding in the design as the basis for task 4.

## 2. Extract the shared predicate model (W1)

- [x] 2.1 Move `IsPredicateExpression`, `IsScalar`, `IsEntityPropertyPath`, and `IsSimpleMappedProperty` from `PrematureQueryMaterializationAnalyzer` into `EfQueryOperationAnalysis`, and verify all existing EFD004 positive/negative fixtures pass unchanged.
- [x] 2.2 Verify no EFD004 finding count, location, or evidence changes as a result of the extraction.

## 3. Add the RowBound model and predicate-bound recognition (W1)

- [x] 3.1 Replace `EfQueryChainAnalysis.HasQueryableTake` with a `RowBound` carrying kind (`Take`, `LocalCollectionMembership`, `KeyEquality`, `KeyEqualityByName`, `TimeWindow`, `None`) and strength (`Strong`, `Medium`, `Weak`, `None`); keep `Take` recognition equivalent to today as the `Take`/`Strong` case.
- [x] 3.2 Recognize `Where(x => localCollection.Contains(x.Prop))` and `Where(x => localCollection.Any(...))` over a local/parameter/field collection as `LocalCollectionMembership`/`Strong`, and verify a fixture matching the production `productIds.Contains` shape is not reported.
- [x] 3.3 Recognize `Where(x => x.Key == value)` key-equality as `KeyEquality`/`Strong` using EF key/foreign-key/index metadata where statically available, and verify PK, FK, and alternate-key fixtures suppress.
- [x] 3.4 Recognize the same equality shape proven only by an `Id`/`<Entity>Id`/`*Id` name heuristic as `KeyEqualityByName`/`Medium` (downgrade, not suppress), and verify a fixture reports at reduced confidence.
- [x] 3.5 Recognize a temporal comparison predicate (`>=`/`>`/`<=`/`<` on a date/time column) as `TimeWindow`/`Weak` (downgrade), and verify a fixture reports at advisory confidence.
- [x] 3.6 Update `UnboundedQueryMaterializationAnalyzer` to report only when the proven chain has no `Strong` bound, preserving all existing client-boundary, arbitrary-`IQueryable`, cross-statement, and EFD004-overlap exclusions; verify the existing `Take` bound fixtures still suppress.

## 4. Make bound-neutral composition outcome-neutral (W2)

- [x] 4.1 Enumerate `AsSplitQuery`, `AsSingleQuery`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`, `Include`, `ThenInclude`, and `TagWith` as bound-neutral pass-through in the chain analysis per the task 1.2 finding.
- [x] 4.2 Add paired fixtures that differ only by a bound-neutral method and verify EFD005 produces identical outcomes for each pair, including the full five-query production shape.
- [x] 4.3 If cross-statement composition is the true differentiator, document the inline-only limitation explicitly in the spec and rule doc and verify neither form depends on a bound-neutral token.

## 5. Universal test-project downgrade in the CLI pipeline (W3)

- [x] 5.1 Add central test-project classification to `WorkspaceAnalyzer`: mark a project as a test project when its compilation references a recognized test framework (`xunit`, `nunit.framework`, `Microsoft.VisualStudio.TestPlatform.*`, `Microsoft.NET.Test.Sdk`) or its MSBuild `IsTestProject` property is set.
- [x] 5.2 Rewrite every mapped `Finding` from a test project to advisory confidence (flowing to `Info` severity via the confidence-driven mapping) with no per-rule code, and verify the downgrade is applied in one shared place rather than inside any analyzer.
- [x] 5.3 Verify the downgrade applies to more than one rule by asserting that findings from at least two different rules (for example EFD001 and EFD005) in a test project are both advisory/`Info`, while the same rules in a production project retain their rule-assigned confidence and `Warning` severity.
- [x] 5.4 Verify a downgraded finding is still reported (never silently suppressed), still counts toward exit code `1`, and remains suppressible only through standard Roslyn suppression.

## 6. Confidence gradient and confidence-driven severity (W5)

- [x] 6.1 Replace the flat `medium` confidence with a computed level from bound strength, best-effort small-lookup and hot-path signals, and test-project context, defaulting to `Medium` when signals are unknown.
- [x] 6.2 Update `DiagnosticFindingMapper` to derive `FindingSeverity` from the reported confidence (`Advisory` → `Info`, otherwise `Warning`), and verify `Medium`/`High` findings remain `Warning` and the JSON schema stays version 1.
- [x] 6.3 Add reporting tests asserting console and JSON severity/confidence for at least one High, one Medium, and one Advisory EFD005 finding, and verify deterministic ordering and exit codes are unchanged.

## 7. Rewrite remediation and impact wording (W4)

- [x] 7.1 Replace the EFD005 remediation with intent-first guidance that removes `Take` as the default suggestion, drops the contradictory arbitrary-limit clause, and names the hydrate-children case as legitimate; keep impact language qualified.
- [x] 7.2 Verify every documented EFD005 example in `docs/rules/EFD005.md` agrees with an analyzer fixture and the new wording.

## 8. Analyzer fixtures and suppression coverage

- [x] 8.1 Assemble at least ten positive EFD005 fixtures spanning `None` bounds, name-heuristic and time-window downgrades, hot-path High confidence, multiple findings, exact spans, and required properties.
- [x] 8.2 Assemble at least ten negative EFD005 fixtures spanning `Take`, `Contains`/`Any` membership, key-equality, bound-neutral composition parity, `AsEnumerable` boundaries, arbitrary `IQueryable`, cross-statement composition, and EFD004 overlap.
- [x] 8.3 Verify `#pragma`, editor-configuration severity, and `SuppressMessage` suppression still exclude EFD005 while unsuppressed neighbors report.

## 9. CLI and end-to-end integration

- [x] 9.1 Extend or add a buildable EFD005 fixture project exercising a suppressed bound, a reported `None` finding, an advisory downgrade, and a test-project downgrade, and verify it builds on .NET 10 with no network access.
- [x] 9.2 Extend process-level console and JSON tests to assert the new severity/confidence, actionable evidence, exact coordinates, JSON purity, schema version 1, exit codes, and repeatable ordering.

## 10. Real-project revalidation and final verification

- [x] 10.1 Re-run EFD005 against the originating real-project corpus and verify the three production false positives are gone and precision is at least 90%. Revalidated against the private codebase: findings dropped 12 → 10, and the production false positives (a compound `Contains` and a null-guarded `Contains(x.Fk.Value)`) are gone with no new findings. The remaining production EFD005 findings are three generic-repository methods that materialize an arbitrary caller-supplied `Expression` predicate, which genuinely has no provable bound: production precision 3/3 = 100%. Required three recognition refinements (conjunction decomposition, null-guarded key paths, unique-single-column index only); see design Decision 7. Fixtures added in tasks 8.1/8.2 shapes.
- [x] 10.2 Verify EFD004 and EFD006 behavior is unchanged by rerunning their fixtures and confirming no new or lost findings.
- [x] 10.3 Build the solution from a clean state with the documented .NET 10 command and verify no errors or warnings.
- [x] 10.4 Run the full automated suite and verify all EFD001–EFD006, EFD011, and CLI end-to-end tests pass.
- [x] 10.5 Run strict OpenSpec validation for `improve-efd005-bound-model` and verify the proposal, modified specs, design, and tasks are coherent and valid.
- [x] 10.6 Manually scan the EFD005 fixture in console, JSON, quiet, and no-color modes and verify actionable evidence, correct severity/confidence, stable exit codes, no ANSI in automation output, and no runtime network or telemetry activity.
