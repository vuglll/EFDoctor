# Proposal

## Why

`AddDbContext` registers a `DbContext` as scoped: one instance per request or scope. A singleton that takes the context in its constructor captures one instance for the application's lifetime:

```csharp
services.AddDbContext<ShopContext>(...);
services.AddSingleton<IPriceCache, PriceCache>(); // PriceCache(ShopContext db)
services.AddHostedService<CleanupWorker>();        // CleanupWorker(ShopContext db)
```

With scope validation on, which is the default in Development, the host fails to start. In Production it doesn't: every request then shares one `DbContext`. That context isn't thread-safe, so the application throws "A second operation was started on this context instance", serves stale tracked data, and grows the change tracker without bound. The roadmap rates this high value with low false-positive risk. Hosted services are the most common case.

## What Changes

- Add **EFD033, `DbContext` captured by a singleton**: `Reliability`, `Warning`, high confidence. It reports a registration call on `IServiceCollection` that registers a singleton:
  - **Which calls:** `AddSingleton`, `TryAddSingleton`, or `AddHostedService`, in generic form (`<TImplementation>` or `<TService, TImplementation>`) or `Type`-argument form, with `typeof`.
  - **Implementation types:** the implementation type is declared in the analyzed source, and every public constructor has a parameter whose type derives from `DbContext`.
  - **Factory registrations:** a singleton registered with a factory lambda, such as `AddSingleton<T>(sp => ...)`, whose body resolves a `DbContext`-derived type from the lambda's provider parameter with `GetRequiredService` or `GetService`. A resolution through a scope that the lambda creates doesn't count.
- **Excluded:**
  - a context type that the same compilation registers with a singleton lifetime, such as `AddDbContext<T>(..., ServiceLifetime.Singleton)`;
  - constructor parameters of `IDbContextFactory<T>`, `IServiceScopeFactory`, or `IServiceProvider`, which are the correct patterns;
  - a type with any public constructor that doesn't take a `DbContext`.

## Capabilities

### New Capabilities

- `efd033-singleton-captures-context`: detection, boundaries, finding contract, suppression, and tests for EFD033.

### Modified Capabilities

None.

## Impact

- **Code:** a new `SingletonCapturesContextAnalyzer`, with registration in `WorkspaceAnalyzer` and the release file.
- **Tests:**
  - analyzer fixtures;
  - `tests/Fixtures/EFD033.Sample` with end-to-end tests;
  - an `EFD005.TestSample` case.
- **Docs:** `docs/rules/EFD033.md`, the rules README, the README, `PACKAGE.md`, usage, suppression, the roadmap, the development history, and the CHANGELOG.
- **Validation:** a corpus re-run, with every finding triaged.
