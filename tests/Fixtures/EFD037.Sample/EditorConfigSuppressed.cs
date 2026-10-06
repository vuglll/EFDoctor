using Microsoft.EntityFrameworkCore;

namespace EFD037.Sample;

public static class EditorConfigSuppressed
{
    public static List<int> Sample(SampleContext context)
    {
        var invoices = context.Invoices.Take(50).ToList();
        return invoices.Select(invoice => invoice.Id).ToList();
    }
}
