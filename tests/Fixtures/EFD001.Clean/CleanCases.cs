using Microsoft.EntityFrameworkCore;

namespace EFD001.Clean;

public sealed class CleanContext : DbContext
{
}

public static class CleanCases
{
    public static void SaveOnce(CleanContext context, IEnumerable<int> items)
    {
        foreach (var _ in items)
        {
            // Mutate tracked entities here.
        }

        context.SaveChanges();
    }
}
