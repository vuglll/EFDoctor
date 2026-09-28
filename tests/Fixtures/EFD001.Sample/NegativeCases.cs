using Microsoft.EntityFrameworkCore;

namespace EFD001.Sample;

public sealed class UnrelatedStore
{
    public void SaveChanges()
    {
    }
}

public static class NegativeCases
{
    public static void SaveOutsideLoop(SampleContext context) => context.SaveChanges();

    public static void UnrelatedInsideLoop(UnrelatedStore store)
    {
        foreach (var _ in Enumerable.Range(0, 1))
        {
            store.SaveChanges();
        }
    }

    public static void DeferredInsideLoop(SampleContext context)
    {
        foreach (var item in Enumerable.Range(0, 1))
        {
            Action deferred = () => context.SaveChanges();
            _ = deferred;
            _ = item;
        }
    }
}
