# efd033-singleton-captures-context Specification

## Purpose
Define high-confidence detection of a dependency-injection singleton, or a hosted service, that captures a scoped `DbContext` for the application's lifetime.

## Requirements

### Requirement: Detect a singleton that captures a DbContext
EFD033 SHALL report an invocation of `AddSingleton`, `TryAddSingleton`, or `AddHostedService` whose receiver, or first extension argument, is of type `Microsoft.Extensions.DependencyInjection.IServiceCollection`, when the singleton captures a `DbContext`.

The singleton captures a `DbContext` when either of these holds:
- **A constructor parameter:** the implementation type is the last generic type argument, or the last `typeof(...)` argument, and every public instance constructor of that type has a parameter whose type derives from `Microsoft.EntityFrameworkCore.DbContext`. The implementation type must be declared in the analyzed source, and have at least one public constructor.
- **A factory lambda:** the registration's factory lambda resolves a type derived from `DbContext` from the lambda's own provider parameter, through `GetRequiredService<T>()`, `GetService<T>()`, or `GetRequiredService(typeof(T))`. A resolution from a scope that the lambda creates doesn't count.

EFD033 SHALL NOT report when the same compilation registers the captured context type with a singleton lifetime. That includes `AddDbContext<T>` or `AddDbContextPool<T>` with a `ServiceLifetime.Singleton` argument, and `AddSingleton<T>` of the context type.

EFD033 SHALL anchor the finding on the registration invocation, and report each registration once.

#### Scenario: Singleton service with a context in its constructor
- **WHEN** `services.AddSingleton<IPriceCache, PriceCache>()` registers a type whose only constructor takes `ShopContext`
- **THEN** EFD033 reports the registration

#### Scenario: Hosted service
- **WHEN** `services.AddHostedService<CleanupWorker>()` registers a type whose constructor takes a `DbContext`-derived type
- **THEN** EFD033 reports the registration

#### Scenario: Type-argument and TryAdd forms
- **WHEN** the registration is `services.AddSingleton(typeof(PriceCache))`, `services.AddSingleton(typeof(IPriceCache), typeof(PriceCache))`, or `services.TryAddSingleton<PriceCache>()`
- **THEN** EFD033 reports the registration

#### Scenario: Factory that resolves the context from the root provider
- **WHEN** `services.AddSingleton<IPriceCache>(sp => new PriceCache(sp.GetRequiredService<ShopContext>()))`
- **THEN** EFD033 reports the registration

#### Scenario: Correct patterns are not reported
- **WHEN** the singleton's constructor takes `IDbContextFactory<ShopContext>`, `IServiceScopeFactory`, or `IServiceProvider`, or its factory resolves the context from a scope it creates
- **THEN** EFD033 does not report

#### Scenario: Scoped and transient registrations
- **WHEN** the type is registered with `AddScoped` or `AddTransient`
- **THEN** EFD033 does not report

#### Scenario: Context registered as a singleton
- **WHEN** the compilation registers the context with `AddDbContext<ShopContext>(options => ..., ServiceLifetime.Singleton)`
- **THEN** EFD033 does not report singletons that take that context

#### Scenario: Alternative constructor without a context
- **WHEN** the implementation type has another public constructor that takes no `DbContext`
- **THEN** EFD033 does not report

### Requirement: Produce actionable EFD033 findings
Each EFD033 finding SHALL have:
- rule title `DbContext captured by a singleton`;
- `Reliability` category, `Warning` severity, and high confidence;
- documentation key `EFD033`;
- the message `Singleton '{implementation}' captures DbContext '{context}' for the application's lifetime`.

Evidence SHALL name the registration method, the implementation, and the constructor parameter, or the factory's resolution. Impact SHALL explain:
- the startup failure under scope validation;
- the shared, non-thread-safe context in production.

Remediation SHALL recommend three fixes:
- `IDbContextFactory<T>`;
- `IServiceScopeFactory`, with a scope per unit of work;
- registering the service as scoped.

#### Scenario: Complete structured finding
- **WHEN** EFD033 reports a registration
- **THEN** the finding has the title, category, severity, confidence, documentation key, message, evidence, impact, and remediation described above

### Requirement: Support standard suppression and existing reporting behavior
EFD033 SHALL use standard Roslyn suppression, and flow through the existing CLI, console and JSON output, and exit codes.

#### Scenario: Suppression
- **WHEN** a matching registration is covered by `#pragma warning disable EFD033`, `[SuppressMessage("Reliability", "EFD033")]`, or `dotnet_diagnostic.EFD033.severity = none`
- **THEN** the finding is excluded

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan of the EFD033 fixture project runs in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD033 findings, with exact locations, JSON schema version `1`, and exit code `1`
