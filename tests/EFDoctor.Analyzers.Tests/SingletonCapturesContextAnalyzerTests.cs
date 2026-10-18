using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class SingletonCapturesContextAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("services.AddSingleton<IPriceCache, PriceCache>();", "PriceCache", "its constructor takes 'ShopContext db'");
        yield return Case("services.AddSingleton<PriceCache>();", "PriceCache", "its constructor takes");
        yield return Case("ServiceCollectionServiceExtensions.AddSingleton<IPriceCache, PriceCache>(services);", "PriceCache", "its constructor takes");
        yield return Case("services.AddSingleton(typeof(PriceCache));", "PriceCache", "its constructor takes");
        yield return Case("services.AddSingleton(typeof(IPriceCache), typeof(PriceCache));", "PriceCache", "its constructor takes");
        yield return Case("services.TryAddSingleton<IPriceCache, PriceCache>();", "PriceCache", "its constructor takes");
        yield return Case("services.AddHostedService<CleanupWorker>();", "CleanupWorker", "its constructor takes 'ShopContext context'");
        yield return Case("services.AddSingleton<IPriceCache, DerivedContextCache>();", "DerivedContextCache", "its constructor takes 'ReportingContext reports'");
        yield return Case("services.AddSingleton<IPriceCache, TwoConstructorCache>();", "TwoConstructorCache", "its constructor takes");
        yield return Case("services.AddSingleton<IPriceCache>(sp => new PriceCache(sp.GetRequiredService<ShopContext>()));", "IPriceCache", "its factory resolves 'ShopContext' from the root service provider 'sp'");
        yield return Case("services.AddSingleton<IPriceCache>(provider => new PriceCache((ShopContext)provider.GetService(typeof(ShopContext))!));", "IPriceCache", "its factory resolves 'ShopContext'");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("services.AddScoped<IPriceCache, PriceCache>();");
        yield return Negative("services.AddTransient<IPriceCache, PriceCache>();");
        yield return Negative("services.AddSingleton<IPriceCache, FactoryCache>();");
        yield return Negative("services.AddSingleton<IPriceCache, ScopeFactoryCache>();");
        yield return Negative("services.AddSingleton<IPriceCache, ProviderCache>();");
        yield return Negative("services.AddSingleton<IPriceCache, OptionalContextCache>();");
        yield return Negative("services.AddSingleton<IPriceCache, NoContextCache>();");
        yield return Negative("services.AddHostedService<ScopedWorker>();");
        yield return Negative("services.AddSingleton<IPriceCache>(sp => { using var scope = sp.CreateScope(); return new PriceCache(scope.ServiceProvider.GetRequiredService<ShopContext>()); });");
        yield return Negative("services.AddSingleton<IPriceCache>(new NoContextCache());");
        yield return Negative("services.AddSingleton<IClock, SystemClock>();");
        yield return Negative("new FakeRegistry().AddSingleton<IPriceCache, PriceCache>();");
    }

    [Theory]
    [Trait("Spec", "efd033-singleton-captures-context/Singleton service with a context in its constructor")]
    [Trait("Spec", "efd033-singleton-captures-context/Hosted service")]
    [Trait("Spec", "efd033-singleton-captures-context/Type-argument and TryAdd forms")]
    [Trait("Spec", "efd033-singleton-captures-context/Factory that resolves the context from the root provider")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsSingletonsThatCaptureAContext(string source, string implementation, string evidence)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.StartsWith($"Singleton '{implementation}' captures DbContext", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains(evidence, diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "efd033-singleton-captures-context/Correct patterns are not reported")]
    [Trait("Spec", "efd033-singleton-captures-context/Scoped and transient registrations")]
    [Trait("Spec", "efd033-singleton-captures-context/Alternative constructor without a context")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportCorrectLifetimes(string source)
    {
        Assert.DoesNotContain(AnalyzerTestHarness.GetCompilationDiagnostics(source), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd033-singleton-captures-context/Context registered as a singleton")]
    public async Task DoesNotReportWhenTheContextIsItselfASingleton()
    {
        var source = Source("""
            services.AddDbContext<ShopContext>(options => { }, ServiceLifetime.Singleton);
            services.AddSingleton<IPriceCache, PriceCache>();
            """);

        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(source, analyzer: new SingletonCapturesContextAnalyzer(), diagnosticId: SingletonCapturesContextAnalyzer.DiagnosticId));
        Assert.Single(await AnalyzerTestHarness.AnalyzeAsync(
            Source("services.AddDbContext<ShopContext>(options => { });\nservices.AddSingleton<IPriceCache, PriceCache>();"),
            analyzer: new SingletonCapturesContextAnalyzer(),
            diagnosticId: SingletonCapturesContextAnalyzer.DiagnosticId));
    }

    [Fact]
    [Trait("Spec", "efd033-singleton-captures-context/Complete structured finding")]
    public async Task FindingCarriesTheCompleteContract()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source("services.AddSingleton<IPriceCache, PriceCache>();")));

        Assert.Equal(SingletonCapturesContextAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("Reliability", diagnostic.Descriptor.Category);
        Assert.Equal("Singleton 'PriceCache' captures DbContext 'ShopContext' for the application's lifetime", diagnostic.GetMessage());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD033", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal(SingletonCapturesContextAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("IDbContextFactory<TContext>", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("IServiceScopeFactory", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Equal("services.AddSingleton<IPriceCache, PriceCache>()", Text(diagnostic));
    }

    [Fact]
    [Trait("Spec", "efd033-singleton-captures-context/Suppression")]
    public async Task HonorsPragmaAndEditorConfig()
    {
        var source = Source("""
            #pragma warning disable EFD033
            services.AddSingleton<IPriceCache, PriceCache>();
            #pragma warning restore EFD033
            services.AddHostedService<CleanupWorker>();
            """);

        Assert.Equal("services.AddHostedService<CleanupWorker>()", Text(Assert.Single(await AnalyzeAsync(source))));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new SingletonCapturesContextAnalyzer(),
            diagnosticId: SingletonCapturesContextAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + Source("services.AddSingleton<IPriceCache, PriceCache>();")));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source.Contains("class ShopContext", StringComparison.Ordinal) ? source : Source(source),
            analyzer: new SingletonCapturesContextAnalyzer(),
            diagnosticId: SingletonCapturesContextAnalyzer.DiagnosticId);

    private static object[] Case(string registration, string implementation, string evidence) => [Source(registration), implementation, evidence];

    private static object[] Negative(string registration) => [Source(registration)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    // AddHostedService is declared here as a stand-in for Microsoft.Extensions.Hosting, which the
    // test compilation doesn't reference; the rule matches it by name on IServiceCollection.
    private static string Source(string registrations) => $$"""
        using System;
        using System.Diagnostics.CodeAnalysis;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.DependencyInjection.Extensions;

        sealed class ShopContext : DbContext { }
        class ReportingContext : DbContext { }
        sealed class AuditContext : ReportingContext { }

        interface IPriceCache { }
        interface IClock { }
        interface IHostedWorker { }

        sealed class PriceCache : IPriceCache { public PriceCache(ShopContext db) { } }
        sealed class DerivedContextCache : IPriceCache { public DerivedContextCache(ReportingContext reports) { } }
        sealed class TwoConstructorCache : IPriceCache
        {
            public TwoConstructorCache(ShopContext db) { }
            public TwoConstructorCache(ShopContext db, IClock clock) { }
        }
        sealed class FactoryCache : IPriceCache { public FactoryCache(IDbContextFactory<ShopContext> factory) { } }
        sealed class ScopeFactoryCache : IPriceCache { public ScopeFactoryCache(IServiceScopeFactory scopes) { } }
        sealed class ProviderCache : IPriceCache { public ProviderCache(IServiceProvider provider) { } }
        sealed class OptionalContextCache : IPriceCache
        {
            public OptionalContextCache(ShopContext db) { }
            public OptionalContextCache(IClock clock) { }
        }
        sealed class NoContextCache : IPriceCache { public NoContextCache() { } }
        sealed class SystemClock : IClock { }
        sealed class CleanupWorker : IHostedWorker { public CleanupWorker(ShopContext context) { } }
        sealed class ScopedWorker : IHostedWorker { public ScopedWorker(IServiceScopeFactory scopes) { } }

        sealed class FakeRegistry { public FakeRegistry AddSingleton<TService, TImplementation>() => this; }

        static class HostingStandIn
        {
            public static IServiceCollection AddHostedService<THostedService>(this IServiceCollection services)
                where THostedService : class, IHostedWorker => services;
        }

        static class Startup
        {
            static void Configure(IServiceCollection services)
            {
                {{registrations}}
            }
        }
        """;
}
