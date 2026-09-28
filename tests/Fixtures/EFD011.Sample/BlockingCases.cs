using Microsoft.EntityFrameworkCore;

namespace EFD011.Sample;

public sealed class Entity
{
    public int Id { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Entity> Entities => Set<Entity>();
}

public static class BlockingCases
{
    public static bool ReportedResult(SampleContext context) =>
        context.Entities.AnyAsync().Result;

    public static void ReportedWait(SampleContext context) =>
        context.SaveChangesAsync().Wait();

    public static Entity? ReportedAwaiter(SampleContext context) =>
        context.Entities.FindAsync(1).GetAwaiter().GetResult();

    public static async Task<bool> ProperAwait(SampleContext context) =>
        await context.Entities.AnyAsync();

#pragma warning disable EFD011 // Reviewed: legacy synchronous integration boundary.
    public static bool PragmaSuppressed(SampleContext context) =>
        context.Entities.AnyAsync().Result;
#pragma warning restore EFD011

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "EFD011",
        Justification = "Legacy synchronous integration boundary.")]
    public static int AttributeSuppressed(SampleContext context) =>
        context.SaveChangesAsync().Result;
}
