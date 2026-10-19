namespace EFD015.Sample;

public static class EditorConfigSuppressed
{
    public static List<Order> Sample(SampleContext context)
    {
        var ids = context.Customers.Select(customer => customer.Id).ToList();
        return context.Orders.Where(order => ids.Contains(order.CustomerId)).Take(50).ToList();
    }
}
