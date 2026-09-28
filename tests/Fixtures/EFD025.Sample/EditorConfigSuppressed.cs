using Microsoft.EntityFrameworkCore;

namespace EFD025.Sample;

public static class EditorConfigSuppressed
{
    public static IQueryable<Order> Run(SampleContext context) =>
        context.Orders.Include(order => order.Customer).Include(order => order.Customer);
}
