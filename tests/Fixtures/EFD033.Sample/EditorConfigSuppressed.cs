using Microsoft.Extensions.DependencyInjection;

namespace EFD033.Sample;

public static class EditorConfigSuppressed
{
    public static void Register(IServiceCollection services) =>
        services.AddSingleton<ReportWriter>();
}

public sealed class ReportWriter(SampleContext context)
{
    public SampleContext Context { get; } = context;
}
