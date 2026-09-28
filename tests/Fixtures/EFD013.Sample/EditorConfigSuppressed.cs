using Microsoft.EntityFrameworkCore;

namespace EFD013.Sample;

public static class EditorConfigSuppressed
{
    public static void Run(SampleContext context)
    {
        var rows = context.Entities.Where(entity => entity.Id > 0).Take(100).ToList();
        foreach (var entity in rows)
        {
            entity.Active = false;
        }

        context.SaveChanges();
    }
}
