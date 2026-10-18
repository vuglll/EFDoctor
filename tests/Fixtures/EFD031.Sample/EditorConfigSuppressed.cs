using Microsoft.EntityFrameworkCore;

namespace EFD031.Sample;

public static class EditorConfigSuppressed
{
    public static List<Order> Sample(SampleContext context) =>
        context.Orders.FromSqlRaw("EXEC dbo.GetOrders").Where(order => order.Open).Take(50).ToList();
}
