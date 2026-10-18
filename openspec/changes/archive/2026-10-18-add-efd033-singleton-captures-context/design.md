# Design

## Context

Registrations are invocations on `IServiceCollection`. The implementation's constructors are visible as symbols when the type is in the same compilation. The context's own lifetime comes from its `AddDbContext` call, which may be in the same project or another one.

## Goals / Non-Goals

**Goals:** report direct captures by singletons and hosted services, with no false positives from the correct factory and scope patterns.

**Non-Goals:**
- transitive captures, such as a singleton that takes a scoped repository that takes the context;
- registrations through assembly scanning or third-party containers;
- implementation types from referenced assemblies.

## Decisions

### Match registrations by name and receiver type

**Decision:** Match the method names `AddSingleton`, `TryAddSingleton`, and `AddHostedService`, where the receiver, or the extension's first parameter, is `IServiceCollection`, and normalize the reduced extension form. Don't bind to the exact extension class, because `AddHostedService` lives in the Hosting package, which a library project may not reference.

**Why:** The names are distinctive on that receiver type. Matching by name keeps the rule working across `Microsoft.Extensions` versions.

### Every public constructor must take a context

**Decision:** Report only when every public instance constructor has a parameter derived from `DbContext`.

**Why:** The container picks the constructor with the most parameters it can satisfy. If one constructor doesn't take the context, the analyzer can't tell which constructor is used.

### Context lifetime exclusion is compilation-wide and lazy

**Decision:** Scan the syntax trees of the compilation lazily, once, for generic `AddDbContext<T>` or `AddDbContextPool<T>` calls with a `ServiceLifetime.Singleton` argument, and for `AddSingleton<T>`/`TryAddSingleton<T>` calls. Collect the simple names of the types they register, and compare a captured context by name. The scan is syntactic, because an analyzer may not request semantic models of other trees (RS1030). That is safe here, because the exclusion only ever removes findings.

**Why:** A context deliberately registered as a singleton makes the singleton consumer consistent. That configuration is a different problem, and outside this rule.

## Risks / Trade-offs

- **[Risk] The context is registered as a singleton in another project.** → The rule would report. This is rare, and suppression is documented.
- **[Trade-off] Transitive captures are missed.** → Accepted for the first version.
