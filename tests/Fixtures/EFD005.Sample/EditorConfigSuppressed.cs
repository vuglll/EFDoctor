namespace EFD005.Sample;

public static class EditorConfigSuppressed
{
    public static List<Customer> Run(SampleContext context) =>
        context.Customers.ToList();
}
