using Microsoft.EntityFrameworkCore;

namespace EFD027.Sample;

public static class EditorConfigSuppressed
{
    public static Task Sample(SampleContext context) =>
        Task.WhenAll(context.Orders.CountAsync(), context.Customers.AnyAsync());
}
