using Microsoft.EntityFrameworkCore;

namespace EFD001.Sample;

public static class CountExistenceCases
{
    public static bool HasEntities(SampleContext context) => context.Entities.Count() > 0;

    public static async Task<bool> HasNoMatchingEntitiesAsync(
        SampleContext context,
        CancellationToken cancellationToken) =>
        (await context.Entities.CountAsync(entity => entity.Id > 0, cancellationToken)) == 0;

    public static bool RequiresExactCount(SampleContext context) => context.Entities.Count() == 1;

    public static bool UnknownProvider(IQueryable<SampleEntity> query) => query.Count() > 0;
}
