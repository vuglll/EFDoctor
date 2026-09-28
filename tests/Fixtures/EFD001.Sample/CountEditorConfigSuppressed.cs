namespace EFD001.Sample;

public static class CountEditorConfigSuppressed
{
    public static bool IsPopulated(SampleContext context) => context.Entities.Count() > 0;
}
