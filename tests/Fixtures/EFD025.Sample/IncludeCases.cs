using Microsoft.EntityFrameworkCore;

namespace EFD025.Sample;

public sealed class Address { public int Id { get; set; } }

public sealed class Customer
{
    public int Id { get; set; }
    public Address Address { get; set; } = new();
}

public sealed class Product { public int Id { get; set; } }

public sealed class Warehouse { public int Id { get; set; } }

public sealed class OrderLine
{
    public int Id { get; set; }
    public int Quantity { get; set; }
    public Product Product { get; set; } = new();
    public Warehouse Warehouse { get; set; } = new();
}

public sealed class Order
{
    public int Id { get; set; }
    public Customer Customer { get; set; } = new();
    public List<OrderLine> Lines { get; set; } = new();
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class IncludeCases
{
    public static IQueryable<Order> ReportedDuplicate(SampleContext context) =>
        context.Orders.Include(order => order.Customer).Include(order => order.Customer);

    public static IQueryable<Order> ReportedCovered(SampleContext context) =>
        context.Orders.Include(order => order.Lines).Include(order => order.Lines).ThenInclude(line => line.Product);

    public static IQueryable<Order> ReportedStringDuplicate(SampleContext context) =>
        context.Orders.Include("Customer.Address").Include("Customer.Address");

    public static IQueryable<Order> BranchingThenIncludes(SampleContext context) =>
        context.Orders.Include(order => order.Lines).ThenInclude(line => line.Product).Include(order => order.Lines).ThenInclude(line => line.Warehouse);

    public static IQueryable<Order> FilteredInclude(SampleContext context) =>
        context.Orders.Include(order => order.Lines.Where(line => line.Quantity > 0)).Include(order => order.Lines);

    public static IQueryable<Order> StringAndExpression(SampleContext context) =>
        context.Orders.Include("Customer").Include(order => order.Customer);

#pragma warning disable EFD025 // Reviewed: the repeated include mirrors a generated query template.
    public static IQueryable<Order> PragmaSuppressed(SampleContext context) =>
        context.Orders.Include(order => order.Customer).Include(order => order.Customer);
#pragma warning restore EFD025

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "EFD025",
        Justification = "The repeated include mirrors a generated query template.")]
    public static IQueryable<Order> AttributeSuppressed(SampleContext context) =>
        context.Orders.Include(order => order.Customer).Include(order => order.Customer);
}
