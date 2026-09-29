using Microsoft.EntityFrameworkCore;

namespace EFD029.Sample;

public static class EditorConfigSuppressed
{
    public static IQueryable<Order> Sample(SampleContext context) =>
        context.Orders.OrderBy(order => order.Id).OrderBy(order => order.CreatedUtc);
}
