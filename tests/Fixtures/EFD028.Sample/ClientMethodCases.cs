using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD028.Sample;

public sealed class Order
{
    public int Id { get; set; }
    public int Status { get; set; }
    public DateTime DueUtc { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class OrderRules
{
    public static bool IsOverdue(Order order) => order.DueUtc < DateTime.UtcNow && order.Status < 3;

    public static int Priority(int status) => status == 2 ? 0 : 1;

    public static DateTime Cutoff(DateTime now) => now.AddDays(-30);
}

public static class ClientMethodCases
{
    // Reported: EF Core can't translate IsOverdue, so the query throws when it runs.
    public static List<Order> ReportedInFilter(SampleContext context) =>
        context.Orders.Where(order => OrderRules.IsOverdue(order)).Take(50).ToList();

    // Reported: the ordering key calls a method of the application.
    public static async Task<List<Order>> ReportedInOrdering(SampleContext context) =>
        await context.Orders.OrderBy(order => OrderRules.Priority(order.Status)).Take(50).ToListAsync();

    // Not reported: the call reads no lambda parameter, so EF Core evaluates it once as a parameter.
    public static List<Order> ParameterOnly(SampleContext context, DateTime now) =>
        context.Orders.Where(order => order.DueUtc < OrderRules.Cutoff(now)).Take(50).ToList();

    // Not reported: the query is materialized first, so the filter runs on the client.
    public static List<Order> AfterMaterialization(SampleContext context) =>
        context.Orders.Take(50).ToList().Where(order => OrderRules.IsOverdue(order)).Take(50).ToList();

    // Not reported: suppressed with a recorded reason.
    [SuppressMessage("Correctness", "EFD028", Justification = "Translated by a query-rewriting plugin.")]
    public static List<Order> Suppressed(SampleContext context) =>
        context.Orders.Where(order => OrderRules.IsOverdue(order)).Take(50).ToList();
}
