using Microsoft.EntityFrameworkCore;

namespace EFD001.Sample;

public sealed class SampleContext : DbContext
{
    public DbSet<SampleEntity> Entities => Set<SampleEntity>();
}

public sealed class SampleEntity
{
    public int Id { get; set; }
}

public static class PositiveCases
{
    public static void SaveItems(SampleContext context, IReadOnlyList<int> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            context.SaveChanges();
        }

        foreach (var item in items)
        {
            if (item > 0)
            {
                context.SaveChanges();
            }
        }
    }

    public static async Task SaveUntilCancelledAsync(SampleContext context, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await context.SaveChangesAsync(cancellationToken);
            break;
        }
    }
}
