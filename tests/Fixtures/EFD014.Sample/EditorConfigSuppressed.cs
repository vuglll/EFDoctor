using Microsoft.EntityFrameworkCore;

namespace EFD014.Sample;

public static class EditorConfigSuppressed
{
    public static List<Order> Sample(SampleContext context) =>
        context.Orders.Skip(50).Take(25).ToList();
}
