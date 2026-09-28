using System.Linq;
using System.Threading.Tasks;

namespace EFD010.Sample;

public static class EditorConfigSuppressed
{
    public static async Task<int> Sample(SampleContext context)
    {
        var count = context.Orders.Count();
        await Task.CompletedTask;
        return count;
    }
}
