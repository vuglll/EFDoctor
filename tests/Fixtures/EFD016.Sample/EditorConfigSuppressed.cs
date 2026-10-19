namespace EFD016.Sample;

public static class EditorConfigSuppressed
{
    public static Task<int> Sample(SampleContext context, CancellationToken cancellationToken) =>
        context.SaveChangesAsync();
}
