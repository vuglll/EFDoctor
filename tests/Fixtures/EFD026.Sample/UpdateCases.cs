using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD026.Sample;

public sealed class Order
{
    public int Id { get; set; }
    public int Status { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class UpdateCases
{
    // Reported: the order is tracked, so Update only widens the UPDATE to every column.
    public static async Task Reported(SampleContext context, int id)
    {
        var order = await context.Orders.FirstAsync(candidate => candidate.Id == id);
        order.Status = 2;
        context.Orders.Update(order);
        await context.SaveChangesAsync();
    }

    // Not reported: a detached entity from the caller.
    public static void Detached(SampleContext context, Order incoming)
    {
        context.Update(incoming);
        context.SaveChanges();
    }

    // Not reported: an untracked load needs Update to be saved.
    public static void Untracked(SampleContext context, int id)
    {
        var order = context.Orders.AsNoTracking().First(candidate => candidate.Id == id);
        order.Status = 2;
        context.Update(order);
        context.SaveChanges();
    }

    // Not reported: suppressed with a recorded reason.
    [SuppressMessage("Performance", "EFD026", Justification = "An audit trigger needs every column written.")]
    public static void Suppressed(SampleContext context, int id)
    {
        var order = context.Orders.First(candidate => candidate.Id == id);
        context.Update(order);
        context.SaveChanges();
    }
}
