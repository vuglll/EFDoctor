# Design

## Context

`EfQueryOperationAnalysis.ClassifyKeyEquality(property)` decides key strength with `HasKeyMetadata(property)`, which reads data annotations only. Row bounds are computed inside `TryAnalyzeSource`, which nine analyzers call. Only EFD005 reads `RowBound`. Fluent configuration lives in other methods and often in other files, so a per-operation analyzer cannot see it without looking at the whole compilation.

## Goals / Non-Goals

**Goals:**
- Prove keys from fluent configuration in the same compilation, and from the navigation foreign-key convention.
- Keep the cost to one lazy scan per compilation, and only when EFD005 actually classifies a key equality.

**Non-Goals:**
- Configuration in referenced assemblies.
- Migration snapshots that use `modelBuilder.Entity("Ns.Type", ...)`.
- The `<PrincipalType>Id` convention, which needs no navigation on the dependent.
- The `Id`/`<Type>Id` primary-key convention. See the proposal: it is unsafe when a composite key is configured out of sight.
- Following `ApplyConfigurationsFromAssembly` or any other indirection. Every `IEntityTypeConfiguration<T>` class in the compilation is scanned anyway, whether or not it is applied.

## Decisions

### Scan the compilation lazily with a semantic model per candidate tree

**Decision:** `EfModelKeys.Build(compilation, cancellationToken)` works in three steps:

1. Walk every syntax tree for `InvocationExpressionSyntax` whose simple name is `HasKey`, `HasAlternateKey`, `HasForeignKey`, or `HasIndex`.
2. Only when a tree has such a call, get its semantic model and resolve the method.
3. Keep calls whose containing namespace is `Microsoft.EntityFrameworkCore` or one of its sub-namespaces.

EFD005 creates it with `Lazy<EfModelKeys>` in its compilation-start action, and first touches it only when classifying a key equality.

**Why:** The other ways to do this are worse:

- **Registering a syntax or operation action for fluent calls and deferring EFD005 to a compilation-end action.** EFD005 findings would become compilation-end diagnostics. Those show only in full-solution analysis in the IDE, and the descriptor would need the `CompilationEnd` tag.
- **Resolving `EntityTypeBuilder<T>` receivers through data flow.** Too costly.

A text-level prefilter keeps the scan cheap. Smartstore has 1,800+ call sites and was analyzed in 21 s.

### Which entity a configuration call applies to

**Decision:**

- **Lambda forms:** the entity is the lambda parameter's type. A column is a member access on that parameter. `e => new { e.A }` with exactly one member is also single-column. More than one member makes the call composite, so it is ignored.
- **String forms** (one argument, a constant string including `nameof`): the entity is determined in this order:
  1. the method's own type argument, as in `HasForeignKey<TDependent>`;
  2. otherwise the `T` of `EntityTypeBuilder<T>` or `OwnedNavigationBuilder<,T>`;
  3. otherwise the dependent (last) type argument of `ReferenceCollectionBuilder<TPrincipal, TDependent>`.

  Any other receiver, including the non-generic `EntityTypeBuilder` used in snapshots, is skipped.
- **`HasIndex`** counts only when an `IsUnique()` or `IsUnique(true)` call appears later in the same fluent chain.

Entries are stored as (entity type, property name). A lookup succeeds for the query's receiver type or any of its base types, so configuration on a TPH base type applies to derived entities.

### Navigation foreign-key convention

**Decision:** The convention holds when all of these are true:
- the property name ends in `Id` and is longer than `Id`;
- the receiver type, or one of its base types, has a property named with the prefix;
- that property's type is a class, is not `string`, and does not implement `IEnumerable`.

This check needs no scan, so it runs inside `HasKeyMetadata`, next to the data annotations.

**Why:** It mirrors EF Core's `ForeignKeyPropertyDiscoveryConvention` for the `<navigation>Id` shape, which is by far the most common.

### Plumbing

**Decision:** `TryAnalyzeSource` gains an optional `EfModelKeys? modelKeys = null` parameter, passed down to `ClassifyKeyEquality`. `TryGetEntityProperty` also returns the receiver type, which a private overload does. The public `IsEntityPropertyPath` is unchanged. Other analyzers keep calling `TryAnalyzeSource` without the new argument.

## Risks / Trade-offs

- **[Risk]** A `<Nav>Id` property that is not actually the FK, because the relationship is configured with a different FK property. → It is still a filter on a navigation-shaped column, and EF would make it a shadow FK anyway. Missing a finding here is the lower cost.
- **[Risk]** Scan cost on very large compilations. → The scan is prefiltered by name, runs at most once per compilation, and runs only when EFD005 reaches a key-equality classification.
- **[Risk]** Configuration defined in one project and used in another stays invisible, as in Jellyfin. → The navigation convention covers the most common cases. Everything else stays advisory, which is documented.
