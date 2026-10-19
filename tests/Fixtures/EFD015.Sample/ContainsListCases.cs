using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD015.Sample;

public sealed class Customer
{
    public int Id { get; set; }
    public string Region { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
}

public static class ContainsListCases
{
    // Reported: the keys make a round trip; a subquery would filter in one statement.
    public static async Task<List<Order>> Reported(SampleContext context, string region)
    {
        var customerIds = await context.Customers.Where(customer => customer.Region == region).Select(customer => customer.Id).ToListAsync();
        return await context.Orders.Where(order => customerIds.Contains(order.CustomerId)).Take(50).ToListAsync();
    }

    // Not reported: the subquery is composed into one statement.
    public static List<Order> Composed(SampleContext context, string region)
    {
        var customerIds = context.Customers.Where(customer => customer.Region == region).Select(customer => customer.Id);
        return context.Orders.Where(order => customerIds.Contains(order.CustomerId)).Take(50).ToList();
    }

    // Not reported: the list is also used on its own.
    public static int Reused(SampleContext context)
    {
        var customerIds = context.Customers.Select(customer => customer.Id).Take(500).ToList();
        return context.Orders.Count(order => customerIds.Contains(order.CustomerId)) + customerIds.Count;
    }

    // Not reported: suppressed with a recorded reason.
    [SuppressMessage("Performance", "EFD015", Justification = "Customers is capped at a few hundred rows.")]
    public static List<Order> Suppressed(SampleContext context)
    {
        var customerIds = context.Customers.Select(customer => customer.Id).ToList();
        return context.Orders.Where(order => customerIds.Contains(order.CustomerId)).Take(50).ToList();
    }
}
