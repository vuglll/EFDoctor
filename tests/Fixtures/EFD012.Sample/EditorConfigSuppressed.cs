using Microsoft.EntityFrameworkCore;

namespace EFD012.Sample;

public static class EditorConfigSuppressed
{
    public static int Run(SampleContext context, string fragment) =>
        context.Database.ExecuteSqlRaw("DELETE FROM Entities " + fragment);
}
