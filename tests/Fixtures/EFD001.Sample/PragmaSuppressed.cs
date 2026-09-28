using Microsoft.EntityFrameworkCore;

namespace EFD001.Sample;

public static class PragmaSuppressed
{
    public static void SaveWithIntentionalBoundary(SampleContext context)
    {
#pragma warning disable EFD001 // Intentional per-item transaction boundary.
        foreach (var _ in Enumerable.Range(0, 1))
        {
            context.SaveChanges();
        }
#pragma warning restore EFD001
    }
}
