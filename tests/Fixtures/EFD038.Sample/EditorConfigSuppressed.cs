using Microsoft.EntityFrameworkCore;

namespace EFD038.Sample;

public static class EditorConfigSuppressed
{
    public static async Task<Product?> Sample(SampleContext context, int id)
    {
        var product = await context.Products.FindAsync(id);
        await context.Products.Where(candidate => candidate.Discontinued).ExecuteDeleteAsync();
        return product;
    }
}
