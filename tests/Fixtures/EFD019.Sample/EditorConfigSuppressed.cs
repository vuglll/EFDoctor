namespace EFD019.Sample;

public static class EditorConfigSuppressed
{
    public static Invoice Run(SampleContext context) =>
        context.Invoices.ToList().First();
}
