using Microsoft.EntityFrameworkCore;

namespace EFD029.Sample;

public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class OrderingCases
{
    // Reported (high): the second OrderBy replaces the first, so orders are sorted by date only.
    public static IQueryable<Order> ReportedOrderByOrderBy(SampleContext context) =>
        context.Orders.OrderBy(order => order.Customer).OrderBy(order => order.CreatedUtc);

    // Reported (high): Where and AsNoTracking don't stop the earlier ordering being discarded.
    public static IQueryable<Order> ReportedThroughFilter(SampleContext context) =>
        context.Orders.OrderBy(order => order.Customer).ThenBy(order => order.Id).Where(order => order.Id > 0).AsNoTracking().OrderByDescending(order => order.CreatedUtc);

    // Not reported: ThenBy adds a secondary sort.
    public static IQueryable<Order> SecondarySort(SampleContext context) =>
        context.Orders.OrderBy(order => order.Customer).ThenBy(order => order.CreatedUtc);

    // Not reported: the first ordering picks the top ten, which are then re-sorted.
    public static IQueryable<Order> ResortTopTen(SampleContext context) =>
        context.Orders.OrderBy(order => order.CreatedUtc).Take(10).OrderBy(order => order.Customer);

    // Not reported: a default ordering overridden through a local is a deliberate pattern.
    public static IQueryable<Order> DefaultThenOverride(SampleContext context, bool byDate)
    {
        IQueryable<Order> query = context.Orders.OrderBy(order => order.Customer);
        if (byDate)
        {
            query = query.OrderBy(order => order.CreatedUtc);
        }

        return query;
    }

#pragma warning disable EFD029 // Reviewed: the override is intended.
    public static IQueryable<Order> PragmaSuppressed(SampleContext context) =>
        context.Orders.OrderBy(order => order.Customer).OrderBy(order => order.CreatedUtc);
#pragma warning restore EFD029

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Correctness",
        "EFD029",
        Justification = "The override is intended.")]
    public static IQueryable<Order> AttributeSuppressed(SampleContext context) =>
        context.Orders.OrderBy(order => order.Customer).OrderBy(order => order.CreatedUtc);
}
