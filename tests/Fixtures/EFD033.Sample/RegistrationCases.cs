using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EFD033.Sample;

public sealed class SampleContext : DbContext
{
}

public interface IPriceCache
{
}

public sealed class PriceCache(SampleContext context) : IPriceCache
{
    public SampleContext Context { get; } = context;
}

public sealed class FactoryPriceCache(IDbContextFactory<SampleContext> factory) : IPriceCache
{
    public IDbContextFactory<SampleContext> Factory { get; } = factory;
}

public static class RegistrationCases
{
    public static void Register(IServiceCollection services)
    {
        services.AddDbContext<SampleContext>();

        // Reported: the singleton keeps one scoped context for the application's lifetime.
        services.AddSingleton<IPriceCache, PriceCache>();

        // Reported: the factory resolves the context from the root provider.
        services.AddSingleton<IPriceCache>(provider => new PriceCache(provider.GetRequiredService<SampleContext>()));

        // Not reported: a scoped service.
        services.AddScoped<IPriceCache, PriceCache>();

        // Not reported: the singleton takes a context factory.
        services.AddSingleton<IPriceCache, FactoryPriceCache>();
    }

    // Not reported: suppressed with a recorded reason.
    [SuppressMessage("Reliability", "EFD033", Justification = "Used only by the single-threaded import tool.")]
    public static void RegisterImportTool(IServiceCollection services) =>
        services.AddSingleton<PriceCache>();
}
