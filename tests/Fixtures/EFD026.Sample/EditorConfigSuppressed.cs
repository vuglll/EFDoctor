namespace EFD026.Sample;

public static class EditorConfigSuppressed
{
    public static void Sample(SampleContext context, int id)
    {
        var order = context.Orders.First(candidate => candidate.Id == id);
        context.Update(order);
        context.SaveChanges();
    }
}
