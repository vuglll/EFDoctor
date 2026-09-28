using System.Linq;

namespace EFD020.Sample;

public static class EditorConfigSuppressed
{
    public static int Sample(SampleContext context)
    {
        var query = context.Orders.Where(order => order.Total > 0);
        var count = query.Count();
        var any = query.Any();
        return any ? count : 0;
    }
}
