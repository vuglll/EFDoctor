using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD032.Sample;

public sealed class Order
{
    public int Id { get; set; }
    public int Status { get; set; }
    public bool Open { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class UntrackedChangeCases
{
    // Reported: the order isn't tracked, so SaveChangesAsync writes nothing.
    public static async Task ReportedSingle(SampleContext context, int id)
    {
        var order = await context.Orders.AsNoTracking().FirstAsync(candidate => candidate.Id == id);
        order.Status = 2;
        await context.SaveChangesAsync();
    }

    // Reported: none of the untracked orders is saved.
    public static void ReportedList(SampleContext context)
    {
        var orders = context.Orders.AsNoTracking().Where(candidate => candidate.Open).Take(100).ToList();
        foreach (var order in orders)
        {
            if (order.Status > 1)
            {
                order.Open = false;
            }
        }

        context.SaveChanges();
    }

    // Not reported: Update attaches the entity, so the change is saved.
    public static void Attached(SampleContext context, int id)
    {
        var order = context.Orders.AsNoTracking().First(candidate => candidate.Id == id);
        order.Status = 2;
        context.Update(order);
        context.SaveChanges();
    }

    // Not reported: a tracked load.
    public static void Tracked(SampleContext context, int id)
    {
        var order = context.Orders.First(candidate => candidate.Id == id);
        order.Status = 2;
        context.SaveChanges();
    }

    // Not reported: suppressed with a recorded reason.
    [SuppressMessage("Correctness", "EFD032", Justification = "An interceptor attaches the entity.")]
    public static void Suppressed(SampleContext context, int id)
    {
        var order = context.Orders.AsNoTracking().First(candidate => candidate.Id == id);
        order.Status = 2;
        context.SaveChanges();
    }
}
