using System.Diagnostics.CodeAnalysis;

namespace EFD001.Sample;

public static class CountAttributeSuppressed
{
    [SuppressMessage("Performance", "EFD002", Justification = "Count retained for provider-specific diagnostics.")]
    public static bool IsPopulated(SampleContext context) => context.Entities.Count() > 0;
}
