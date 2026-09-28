using Microsoft.EntityFrameworkCore;

namespace EFD013.Sample;

public sealed class Entity
{
    public int Id { get; set; }
    public int Status { get; set; }
    public bool Active { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Entity> Entities => Set<Entity>();
}

public static class BulkOperationCases
{
    public static void ReportedUpdate(SampleContext context, int status)
    {
        var rows = context.Entities.Where(entity => entity.Id > 0).Take(100).ToList();
        foreach (var entity in rows)
        {
            entity.Status = status;
            entity.Active = false;
        }

        context.SaveChanges();
    }

    public static async Task ReportedDeleteAsync(SampleContext context)
    {
        var rows = await context.Entities.Where(entity => entity.Id > 0).Take(100).ToListAsync();
        foreach (var entity in rows)
        {
            context.Remove(entity);
        }

        await context.SaveChangesAsync();
    }

    public static void ExcludedEntityDependentUpdate(SampleContext context)
    {
        var rows = context.Entities.Where(entity => entity.Id > 0).Take(100).ToList();
        foreach (var entity in rows)
        {
            entity.Status = entity.Status + 1;
        }

        context.SaveChanges();
    }

#pragma warning disable EFD013 // Reviewed: tracked callbacks are required.
    public static void PragmaSuppressed(SampleContext context)
    {
        var rows = context.Entities.Where(entity => entity.Id > 0).Take(100).ToList();
        foreach (var entity in rows)
        {
            entity.Active = false;
        }

        context.SaveChanges();
    }
#pragma warning restore EFD013

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "EFD013",
        Justification = "Tracked callbacks are required.")]
    public static void AttributeSuppressed(SampleContext context)
    {
        var rows = context.Entities.Where(entity => entity.Id > 0).Take(100).ToList();
        foreach (var entity in rows)
        {
            context.Remove(entity);
        }

        context.SaveChanges();
    }
}
