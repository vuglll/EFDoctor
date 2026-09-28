using Microsoft.EntityFrameworkCore;

namespace EFD001.Sample;

public static class EditorConfigSuppressed
{
    public static void SaveWithConfiguredSuppression(SampleContext context)
    {
        foreach (var _ in Enumerable.Range(0, 1))
        {
            context.SaveChanges();
        }
    }
}
