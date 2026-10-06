namespace EFD002.Sample;

public static class EditorConfigSuppressed
{
    public static bool Sample(SampleContext context, int id)
    {
        var product = context.Products.FirstOrDefault(candidate => candidate.Id == id);
        return product is not null;
    }
}
