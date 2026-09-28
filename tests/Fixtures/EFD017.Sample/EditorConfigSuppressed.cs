using Microsoft.EntityFrameworkCore;

namespace EFD017.Sample;

public static class EditorConfigSuppressed
{
    public static IQueryable<int> Run(SampleContext context) =>
        context.Orders.Include(order => order.Customer).Select(order => order.Id);
}
