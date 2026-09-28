using Microsoft.EntityFrameworkCore;

namespace EFD011.Sample;

public static class EditorConfigSuppressed
{
    public static bool Run(SampleContext context) =>
        context.Entities.AnyAsync().Result;
}
