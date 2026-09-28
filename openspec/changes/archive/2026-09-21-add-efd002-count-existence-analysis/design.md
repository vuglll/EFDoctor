# Design

## Context

See `proposal.md` for motivation and `specs/efd002-count-existence/spec.md` for the behavior contract. The repository already has a `netstandard2.0` Roslyn analyzer assembly, a .NET 10 CLI that runs one analyzer through `MSBuildWorkspace`, a shared finding/report model, deterministic console and JSON renderers, and focused plus process-level tests.

Two current seams need modest generalization. `WorkspaceAnalyzer` constructs and filters only `SaveChangesInLoopAnalyzer`, and `DiagnosticFindingMapper` reads shared diagnostic-property names through EFD001 constants. EFD002 must use those same contracts without making either component rule-specific.

## Goals / Non-Goals

**Goals:**

- Add a second independent Roslyn diagnostic analyzer while preserving the existing analyzer-compatible target and CLI behavior.
- Make EFD002 matching semantic, explainable, conservative about EF query provenance, and linear in the size of the inspected expression chain.
- Reuse one stable metadata contract for mapping every EFDoctor diagnostic into findings.
- Keep EFD001 behavior and all existing output/exit-code contracts unchanged.

**Non-Goals:**

- General data-flow, interprocedural query-origin tracking, or runtime provider inference.
- Recognizing count properties, LINQ-to-Objects, `LongCount`, pattern-matching syntax, or arbitrary mathematically equivalent expressions in this iteration.
- Producing an automatic code fix or rewriting predicates.
- Changing the JSON schema, CLI syntax, severity model, or suppression mechanism.

## Decisions

### 1. Implement EFD002 as a separate invocation-operation analyzer

Add a `CountUsedForExistenceAnalyzer` with diagnostic ID EFD002. Resolve the metadata symbols for `System.Linq.Queryable`, `Microsoft.EntityFrameworkCore.DbSet<T>`, and `Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions` once in a compilation-start action, then register an `OperationKind.Invocation` action only when the required symbols are present.

For extension calls, normalize the target method through `ReducedFrom` when present and compare declaring types and method symbols with `SymbolEqualityComparer.Default`. A synchronous candidate must be a `Queryable.Count` overload; an asynchronous candidate must be an EF Core `CountAsync` overload. Method-name checks serve only as an early filter and never establish a match.

Alternative considered: add EFD002 branches to `SaveChangesInLoopAnalyzer`. Rejected because rule identity, metadata, tests, and matching logic should remain independently evolvable.

### 2. Prove synchronous EF provenance by following only the query-source chain

For `Queryable.Count`, extract its source operation and recursively unwrap parentheses and implicit conversions. A source is proven EF-backed when its static type is `DbSet<T>` or derives from that generic definition, or when it is a supported query-shaping invocation whose own source chain reaches such an operation. This covers direct context properties, `DbContext.Set<T>()`, and inline chains such as `context.Entities.Where(...).Select(...).Count()`.

Traversal follows only the receiver/source argument of query-shaping operations. It does not scan predicate bodies or arbitrary child operations, and it stops at locals, fields, properties, or parameters statically typed only as `IQueryable<T>`. This prevents a coincidental `DbSet` elsewhere in an expression from establishing provenance and makes the documented unknown-provider limitation intentional.

EF Core `CountAsync` is already provider-specific by resolved declaring type, so it does not require a separate `DbSet` root proof. Calls that fail semantic resolution are ignored.

Alternative considered: report every `Queryable.Count` existence comparison. Rejected because custom and non-EF query providers would create false positives. Alternative considered: track assignments and aliases to prove arbitrary `IQueryable` origins. Deferred because it requires data-flow analysis and is not necessary for the high-confidence first version.

### 3. Normalize only direct binary existence comparisons

Starting at the matching invocation, walk through transparent wrappers: implicit conversions, parentheses represented by syntax/operations, and a single await for `CountAsync`. The resulting operation must be one operand of an `IBinaryOperation`, and the other operand must have a compile-time integral constant value of zero or one.

Normalize operand ordering so the count is conceptually on the left, reversing relational operators when needed. Match exactly these normalized forms:

| Meaning | Supported normalized forms | Suggested replacement |
|---|---|---|
| Has rows | `count > 0`, `count != 0`, `count >= 1` | `Any(...)` / `await AnyAsync(...)` |
| Has no rows | `count == 0`, `count <= 0`, `count < 1` | `!Any(...)` / `!await AnyAsync(...)` |

The comparison classifier returns both polarity and normalized form for diagnostic evidence. It rejects `count == 1`, thresholds above one, arithmetic around the count, variables assigned from the count, and other consumers.

Alternative considered: algebraically simplify arbitrary expressions. Rejected because it expands complexity and risks changing meaning around conversions or user-defined operators. Alternative considered: include relational-pattern syntax now. Deferred until demand justifies a separate, testable extension to the contract.

### 4. Emit rule metadata through shared diagnostic-property keys

Move the property-name constants for confidence, evidence, likely impact, remediation, and documentation reference into an analyzer-internal shared contract used by both analyzers and the CLI mapper. Keep their serialized names unchanged so EFD001 output does not change.

EFD002 reports on the complete `Count` or `CountAsync` invocation syntax and emits one warning with high confidence. Evidence records the resolved method and normalized existence comparison. Impact states that counting solely for existence can make the database process more matches than a short-circuiting existence query. Remediation selects `Any`/`AnyAsync` or its negation and notes that a predicate overload should preserve the predicate. Wording avoids claiming measured cost or that all providers execute identically.

Alternative considered: let the mapper switch on each rule ID to obtain rule-specific metadata. Rejected because it duplicates analyzer knowledge and makes adding rules require CLI mapping edits.

### 5. Run an immutable analyzer set and map all EFDoctor diagnostics generically

Replace the single analyzer instance in `WorkspaceAnalyzer` with one immutable array containing EFD001 and EFD002 analyzers. Run the set once per C# compilation, retain effective non-suppressed diagnostics whose IDs belong to the registered EFDoctor rules, map them through the shared property contract, deduplicate using the existing finding key, and apply the existing deterministic ordering at rendering time.

The JSON schema remains version 1 because the envelope and finding shape do not change; only a new documented rule ID can appear. Existing exit codes remain `0` for no findings, `1` for one or more findings, and `2` for invalid input or analysis failure.

Alternative considered: execute each analyzer separately. Rejected because Roslyn can efficiently execute an analyzer set while preserving per-diagnostic identity and suppression.

### 6. Generalize the analyzer test harness and add mixed-rule process coverage

Allow focused tests to supply the analyzer under test and configured diagnostic ID while retaining the shared EF Core compilation references. Add at least ten positive and ten negative EFD002 fixtures covering all normalized comparisons, operand reversal, sync/async and predicate overloads, composed queries, false-positive boundaries, multiple calls, exact source spans, and standard suppression.

Add EFD002 to a buildable CLI fixture, or add a dedicated EFD002 fixture if that keeps expected output clearer. Process-level tests verify console and JSON fields, schema version 1, exit code 1, and deterministic ordering when EFD001 and EFD002 coexist. Existing EFD001 and clean/invalid-input tests remain regression coverage.

Alternative considered: validate EFD002 only with unit tests. Rejected because the second rule must prove that analyzer registration, generic mapping, suppression, and both renderers remain reusable.

### 7. Document rule scope and suppression beside EFD001

Add `docs/rules/EFD002.md` with triggering and non-triggering examples, performance rationale, equivalent `Any`/`AnyAsync` remediation for positive and empty tests, the unknown-`IQueryable` and stored-value limitations, and pragma/editor-configuration/`SuppressMessage` examples with justification. Update README rule documentation links and analyzer release tracking without changing the CLI usage contract.

## Risks / Trade-offs

- **[A query assigned to an `IQueryable<T>` local may be EF-backed but not reported]** → Prefer a false negative over an unprovable provider match; document the boundary and revisit with data-flow analysis only if real usage warrants it.
- **[Query-source traversal could accidentally inspect a non-source argument]** → Centralize source extraction for reduced and static extension forms and test predicates containing unrelated EF expressions.
- **[Roslyn operation shapes differ around reduced extensions, awaits, and conversions]** → Test fluent/static invocation forms and wrapper combinations against the pinned Roslyn version.
- **[A provider may optimize `Count` so the practical cost is small]** → Phrase impact as a possibility and recommendation as intent-preserving guidance, not a measured guarantee.
- **[Adding a second analyzer exposes rule-specific assumptions in CLI code]** → Generalize registration and property keys before enabling EFD002, with EFD001 regression tests protecting output compatibility.
- **[Diagnostic ordering can shift when two rules share a location]** → Preserve the existing path/span/rule-ID sort and assert mixed-rule order in reporting and process tests.

## Migration Plan

1. Extract the shared diagnostic-property key contract and generalize test helpers without changing EFD001 behavior.
2. Add EFD002 semantic matching, comparison normalization, metadata, and focused tests.
3. Register both analyzers in the CLI and extend mapping/reporting integration tests.
4. Add or update the process fixture, rule documentation, README links, and analyzer release notes.
5. Run strict OpenSpec validation, the full automated test suite, a clean solution build, and manual console/JSON fixture scans.

No persisted data or report migration is required. Rollback removes EFD002 and restores the analyzer registration set; schema-version-1 consumers remain compatible because the finding shape is unchanged.
