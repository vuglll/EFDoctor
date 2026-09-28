# Design

## Context

See `proposal.md` for motivation. EFDoctor analyzers resolve EF Core marker types in a compilation-start action and register per-operation or (new here) per-symbol actions. `EfQueryOperationAnalysis.IsOrDerivesFrom(ITypeSymbol?, INamedTypeSymbol)` already walks base types and is reused for the `DbContext` check. Unlike the query rules, EFD021 inspects declarations, not query chains, so it does not touch `EfQueryOperationAnalysis.TryAnalyzeSource` or any shared query machinery — it is independent of the other analyzers.

## Goals / Non-Goals

**Goals:**

- Detect a `static` field of a `DbContext`-derived type with near-zero false positives.
- Keep the analyzer self-contained and orthogonal to the query analyzers.

**Non-Goals:**

- Detecting a `DbContext` held by a dependency-injection singleton (a "long-lived" instance with no `static` field); that needs DI-registration analysis and carries higher false-positive risk.
- Flagging static properties. A `static` auto-property is the same defect, but a computed `static` property (`=> provider.GetRequiredService<Ctx>()`) is safe; distinguishing them adds risk, so properties are deferred.
- Judging whether a given static usage is "actually" thread-safe; the shape alone is the signal.

## Decisions

### 1. Symbol action over field symbols

Register `context.RegisterSymbolAction(..., SymbolKind.Field)` from the compilation-start action after resolving `Microsoft.EntityFrameworkCore.DbContext`. Report when the field is `static`, not `const`, not compiler-synthesized (`IsImplicitlyDeclared` is false), and its type `IsOrDerivesFrom` `DbContext`. Anchor the diagnostic on the field's declaring location.

A symbol action (rather than a syntax action) gives resolved type and modifier information directly and reports once per field symbol even when several fields share a declaration statement.

Alternative considered: an operation/syntax action over field declarations. Rejected: symbols already carry `IsStatic`/`IsConst`/type resolution, and the symbol action is simpler and deduplicated.

### 2. Confidence and framing

High confidence: the shape is unambiguous and statically decidable. Category is reliability/thread-safety; severity `Warning`, mapped from high confidence. The central test-project downgrade still applies, so a static `DbContext` in a test helper is reported at `Info`.

### 3. Deliberate exclusions to protect trust

Instance fields, `const` fields (cannot be a `DbContext` anyway, but excluded explicitly), synthesized backing fields, and non-`DbContext` types are excluded. `[ThreadStatic]` static fields are still reported (a per-thread `DbContext` remains an anti-pattern) but can be suppressed with a recorded reason; this keeps the rule simple without special-casing.

## Risks / Trade-offs

- **[A static DbContext that is provably single-threaded and short-lived is flagged]** → Reported, not suppressed silently; the developer can suppress with a recorded reason. This is rare and worth surfacing.
- **[DI-singleton DbContexts are missed]** → Accepted and documented as out of scope; a follow-up rule can add DI-lifetime analysis without changing this one.

## Migration Plan

1. Add `StaticDbContextFieldAnalyzer` (EFD021) with a field symbol action.
2. Register it in `WorkspaceAnalyzer` and add the `AnalyzerReleases.Unshipped.md` entry.
3. Add analyzer fixtures/tests and a buildable CLI fixture with end-to-end coverage.
4. Add `docs/rules/EFD021.md`, the README rule-table row, and a CHANGELOG entry.
5. Sync the new `efd021-static-dbcontext-field` spec and archive.

No persisted data or schema change. Rollback removes the analyzer and its registration.
