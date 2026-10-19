using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD036.Sample;

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Email { get; set; } = "";
    public string Reference { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class FilterCases
{
    // Reported: no index leads with Email.
    public static List<Order> ReportedEmail(SampleContext context, string email) =>
        context.Orders.Where(order => order.Email == email).Take(50).ToList();

    // Reported: a range filter on an unindexed column.
    public static Task<int> ReportedRange(SampleContext context, DateTime since) =>
        context.Orders.CountAsync(order => order.CreatedUtc >= since);

    // Not reported: CustomerId leads an index.
    public static List<Order> IndexedCustomer(SampleContext context, int customerId, string email) =>
        context.Orders.Where(order => order.CustomerId == customerId && order.Email == email).Take(50).ToList();

    // Not reported: the primary key.
    public static Order? ByKey(SampleContext context, int id) =>
        context.Orders.FirstOrDefault(order => order.Id == id);

    // Not reported: suppressed with a recorded reason.
    [SuppressMessage("Performance", "EFD036", Justification = "Admin-only search over a small table.")]
    public static List<Order> Suppressed(SampleContext context, string email) =>
        context.Orders.Where(order => order.Email == email).Take(50).ToList();
}
