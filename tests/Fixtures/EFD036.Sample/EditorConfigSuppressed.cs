namespace EFD036.Sample;

public static class EditorConfigSuppressed
{
    public static List<Order> Sample(SampleContext context, string reference) =>
        context.Orders.Where(order => order.Reference == reference).Take(50).ToList();
}
