namespace EFD034.Sample;

public static class EditorConfigSuppressed
{
    public static void CreateDemoDatabase(ShopContext context) => context.Database.EnsureCreated();
}
