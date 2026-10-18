namespace EFD030.Sample;

public static class EditorConfigSuppressed
{
    public static IQueryable<Order> Sample()
    {
        using var db = new SampleContext();
        return db.Orders;
    }
}
