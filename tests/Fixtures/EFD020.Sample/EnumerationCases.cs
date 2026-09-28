using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace EFD020.Sample;

public sealed class Order
{
    public int Id { get; set; }
    public decimal Total { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class EnumerationCases
{
    // Reported: the same deferred query is executed twice (Count then Any).
    public static int ReportedTwice(SampleContext context)
    {
        var query = context.Orders.Where(order => order.Total > 0);
        var count = query.Count();
        var any = query.Any();
        return any ? count : 0;
    }

    // Not reported: the query is executed exactly once.
    public static int SingleExecution(SampleContext context)
    {
        var query = context.Orders.Where(order => order.Total > 0);
        return query.Count();
    }

    public static int PragmaSuppressed(SampleContext context)
    {
#pragma warning disable EFD020 // Reviewed: the second enumeration intentionally re-queries after a save.
        var query = context.Orders.Where(order => order.Total > 0);
#pragma warning restore EFD020
        var count = query.Count();
        var any = query.Any();
        return any ? count : 0;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "EFD020",
        Justification = "The second enumeration intentionally re-queries after a save.")]
    public static int AttributeSuppressed(SampleContext context)
    {
        var query = context.Orders.Where(order => order.Total > 0);
        var count = query.Count();
        var any = query.Any();
        return any ? count : 0;
    }
}
