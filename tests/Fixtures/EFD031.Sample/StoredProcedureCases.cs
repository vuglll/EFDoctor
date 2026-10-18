using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD031.Sample;

public sealed class Order
{
    public int Id { get; set; }
    public bool Open { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class StoredProcedureCases
{
    // Reported: EF Core can't wrap the procedure call in a subquery to add the filter.
    public static List<Order> ReportedFilter(SampleContext context, int customerId) =>
        context.Orders.FromSqlRaw("EXEC dbo.GetOrders @p0", customerId).Where(order => order.Open).Take(50).ToList();

    // Reported: FirstOrDefaultAsync composes a TOP(1) over the call.
    public static Task<Order?> ReportedFirst(SampleContext context, int id) =>
        context.Orders.FromSql($"EXEC dbo.GetOrder {id}").FirstOrDefaultAsync();

    // Not reported: the result is materialized, then filtered on the client.
    public static List<Order> MaterializedFirst(SampleContext context) =>
        context.Orders.FromSqlRaw("EXEC dbo.GetOrders").AsEnumerable().Where(order => order.Open).ToList();

    // Not reported: a table-valued function is composable.
    public static List<Order> Composable(SampleContext context, int customerId) =>
        context.Orders.FromSqlRaw("SELECT * FROM dbo.GetOrdersFn({0})", customerId).Where(order => order.Open).Take(50).ToList();

    // Not reported: suppressed with a recorded reason.
    [SuppressMessage("Correctness", "EFD031", Justification = "Legacy provider composes procedure output.")]
    public static List<Order> Suppressed(SampleContext context) =>
        context.Orders.FromSqlRaw("EXEC dbo.GetOrders").Where(order => order.Open).Take(50).ToList();
}
