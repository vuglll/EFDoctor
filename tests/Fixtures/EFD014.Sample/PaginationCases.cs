using Microsoft.EntityFrameworkCore;

namespace EFD014.Sample;

public sealed class Order
{
    public int Id { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class PaginationCases
{
    // Reported (high): Skip with no ordering, returned as a query so only EFD014 applies.
    public static IQueryable<Order> ReportedSkip(SampleContext context) =>
        context.Orders.Skip(50);

    // Reported (high): Skip + Take with no ordering; one finding anchored on the Take.
    public static List<Order> ReportedSkipTake(SampleContext context) =>
        context.Orders.Skip(50).Take(25).ToList();

    // Not reported: a bare Take is a top-N shape, frequently intentional.
    public static List<Order> TakeOnly(SampleContext context) =>
        context.Orders.Take(25).ToList();

    // Not reported: a stable ordering precedes the paging operators.
    public static List<Order> OrderedPaging(SampleContext context) =>
        context.Orders.OrderBy(order => order.Id).Skip(50).Take(25).ToList();

#pragma warning disable EFD014 // Reviewed: intentional unordered sample for a smoke test.
    public static List<Order> PragmaSuppressed(SampleContext context) =>
        context.Orders.Skip(50).Take(25).ToList();
#pragma warning restore EFD014

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Correctness",
        "EFD014",
        Justification = "Intentional unordered sample for a smoke test.")]
    public static List<Order> AttributeSuppressed(SampleContext context) =>
        context.Orders.Skip(50).Take(25).ToList();
}
