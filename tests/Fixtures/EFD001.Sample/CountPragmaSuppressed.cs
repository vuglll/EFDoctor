namespace EFD001.Sample;

public static class CountPragmaSuppressed
{
    public static bool IsPopulated(SampleContext context)
    {
#pragma warning disable EFD002 // Count retained for provider-specific diagnostics.
        return context.Entities.Count() > 0;
#pragma warning restore EFD002
    }
}
