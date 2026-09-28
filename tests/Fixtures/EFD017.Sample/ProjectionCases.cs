using Microsoft.EntityFrameworkCore;

namespace EFD017.Sample;

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }
    public decimal Total { get; set; }
    public Customer Customer { get; set; } = new();
    public List<OrderLine> Lines { get; set; } = new();
}

public sealed class OrderLine { public int Id { get; set; } }

public sealed record OrderSummary(int Id, string CustomerName);

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class ProjectionCases
{
    public static IQueryable<int> ReportedScalar(SampleContext context) =>
        context.Orders.Include(order => order.Lines).Select(order => order.Id);

    public static IQueryable<OrderSummary> ReportedDto(SampleContext context) =>
        context.Orders.Include(order => order.Customer).Where(order => order.Total > 0).Select(order => new OrderSummary(order.Id, order.Customer.Name));

    public static IQueryable<Order> EntityPreserved(SampleContext context) =>
        context.Orders.Include(order => order.Customer).Select(order => order);

    public static IQueryable<Customer> AmbiguousNavigation(SampleContext context) =>
        context.Orders.Include(order => order.Customer).Select(order => order.Customer);

#pragma warning disable EFD017 // Reviewed: the include documents the aggregate boundary.
    public static IQueryable<int> PragmaSuppressed(SampleContext context) =>
        context.Orders.Include(order => order.Customer).Select(order => order.Id);
#pragma warning restore EFD017

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Correctness",
        "EFD017",
        Justification = "The include documents the aggregate boundary.")]
    public static IQueryable<int> AttributeSuppressed(SampleContext context) =>
        context.Orders.Include(order => order.Customer).Select(order => order.Id);
}
