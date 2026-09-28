using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace EFD010.Sample;

public sealed class Order
{
    public int Id { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class SyncCallCases
{
    // Reported: synchronous Count in an async method (CountAsync is available).
    public static async Task<int> ReportedCount(SampleContext context)
    {
        var count = context.Orders.Count();
        await Task.CompletedTask;
        return count;
    }

    // Reported: synchronous SaveChanges in an async method.
    public static async Task ReportedSaveChanges(SampleContext context)
    {
        context.SaveChanges();
        await Task.CompletedTask;
    }

    // Reported: synchronous Find in an async method.
    public static async Task<Order?> ReportedFind(SampleContext context, int id)
    {
        var order = context.Orders.Find(id);
        await Task.CompletedTask;
        return order;
    }

    // Not reported: a synchronous method has no awaitable context.
    public static int SyncMethod(SampleContext context) => context.Orders.Count();

    // Not reported: already asynchronous.
    public static async Task<int> AlreadyAsync(SampleContext context) =>
        await context.Orders.CountAsync();

    public static async Task<int> PragmaSuppressed(SampleContext context)
    {
#pragma warning disable EFD010 // Reviewed: startup seeding runs off the request path.
        var count = context.Orders.Count();
#pragma warning restore EFD010
        await Task.CompletedTask;
        return count;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "EFD010",
        Justification = "Startup seeding runs off the request path.")]
    public static async Task<int> AttributeSuppressed(SampleContext context)
    {
        var count = context.Orders.Count();
        await Task.CompletedTask;
        return count;
    }
}
