using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD001.Sample;

public static class AttributeSuppressed
{
    [SuppressMessage("Performance", "EFD001", Justification = "Each item is an intentional independent transaction.")]
    public static void SaveWithIndependentTransactions(SampleContext context)
    {
        foreach (var _ in Enumerable.Range(0, 1))
        {
            context.SaveChanges();
        }
    }
}
