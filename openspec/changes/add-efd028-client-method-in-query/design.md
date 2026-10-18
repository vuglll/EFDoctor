# Design

## Context

EFD022 already reports one untranslatable call shape inside a query lambda. It walks up from the call to the enclosing lambda and its operator, and proves the operator's source. EFD028 has the same structure with a different trigger, and uses the origin proof added by `improve-query-chain-conditional-locals`.

## Goals / Non-Goals

**Goals:** high precision on the most common untranslatable shape, an application method that reads the entity inside a filter or ordering.

**Non-Goals:**
- projections;
- methods from referenced assemblies, whose translatability EFDoctor can't judge without a list of translatable framework members;
- unmapped computed properties;
- `Func` delegate invocations.

## Decisions

### Entry point: the method invocation, not the operator

**Decision:** Register on `OperationKind.Invocation`.
1. Accept only a target whose original definition has a declaring syntax reference in the compilation, and which isn't excluded.
2. Walk up to each enclosing `IAnonymousFunctionOperation`. Above the outermost lambda, find the invocation whose argument holds it. That is the query operator.
3. Require that operator to be one of the translated operators, and prove its source.

The operator is the first invocation above a lambda that is itself an argument. The walk stops at the first lambda whose consuming invocation is a translated operator over a proven EF source. A lambda consumed by an `Enumerable` operator inside the query, such as `o.Lines.Any(l => …)`, is skipped, and the walk continues upward.

**Why:** It reports each offending call once, at its own span, wherever it is nested, matching EFD022.

### The call must read a query lambda parameter

**Decision:** Collect the parameters of every lambda between the invocation and the operator, inclusive. Report only when the instance or an argument of the invocation has a descendant parameter reference to one of them.

**Why:** EF Core's funcletizer evaluates subtrees that don't depend on lambda parameters on the client, as query parameters. `Where(o => o.DueUtc < Cutoff(now))` is legal and common. Without this check, the rule would report a large class of false positives.

### Exclusions by attribute name, registration, and stub shape

**Decision:** Exclude:
- methods with the attribute class names in the spec, compared by simple name so any library version matches;
- static methods on `DbContext` subtypes;
- bodies that only throw, either `=> throw …` or a block with one `throw` statement;
- names found in `HasDbFunction` arguments. A lazy, compilation-scoped scan of the syntax trees collects identifier and string-literal tokens inside the argument lists of `HasDbFunction` invocations. It is computed once per compilation.

**Why:**
- A database function can be registered in another project, but its stub almost always lives on the context, or throws.
- Query-rewriting libraries mark their methods with an attribute, or need an extra operator in the chain (LinqKit's `AsExpandable`, DelegateDecompiler's `Decompile`). An extra operator already breaks the chain proof.

**Alternatives considered:**
- A list of translatable BCL members, so metadata methods could be reported too. Rejected for the first version: it's large, provider-specific, and changes with each EF Core release.

### Severity and confidence

`Warning`, high confidence, `Correctness`, as for EFD022. The failure is deterministic whenever the query executes.

## Risks / Trade-offs

- **[Risk] A provider plugin translates a source method that EFDoctor doesn't know about.** → The corpus re-run triages every finding. Suppression is documented, and the attribute list can grow.
- **[Risk] A method referenced only through `HasDbFunction` in another project, with a real body, not on the context.** → This is rare. It's a documented limitation, and it can be suppressed.
- **[Trade-off] Methods from a referenced domain library are missed.** → Accepted for precision. It's a documented boundary.
