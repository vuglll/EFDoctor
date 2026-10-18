using Microsoft.EntityFrameworkCore;

namespace EFD032.Sample;

public static class EditorConfigSuppressed
{
    public static void Sample(SampleContext context, int id)
    {
        var order = context.Orders.AsNoTracking().First(candidate => candidate.Id == id);
        order.Status = 3;
        context.SaveChanges();
    }
}
