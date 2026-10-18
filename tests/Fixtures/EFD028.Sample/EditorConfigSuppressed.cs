using Microsoft.EntityFrameworkCore;

namespace EFD028.Sample;

public static class EditorConfigSuppressed
{
    public static List<Order> Sample(SampleContext context) =>
        context.Orders.Where(order => OrderRules.IsOverdue(order)).Take(50).ToList();
}
