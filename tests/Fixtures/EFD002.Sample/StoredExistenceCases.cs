using Microsoft.EntityFrameworkCore;

namespace EFD002.Sample;

public sealed class Product
{
    public int Id { get; set; }
    public string? Description { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
}

public static class StoredExistenceCases
{
    // Reported (high): the count is stored, and then only compared for existence.
    public static bool ReportedStoredCount(SampleContext context)
    {
        var products = context.Products.Count(product => product.Id > 100);
        return products > 0;
    }

    // Reported (high): the same with an awaited count and an empty-set test.
    public static async Task<bool> ReportedStoredCountAsync(SampleContext context)
    {
        var products = await context.Products.CountAsync();
        return products == 0;
    }

    // Reported (medium): the product is loaded only to learn whether it exists.
    public static bool ReportedStoredEntity(SampleContext context, int id)
    {
        var product = context.Products.FirstOrDefault(candidate => candidate.Id == id);
        return product is not null;
    }

    // Reported (medium): the same check written in one expression.
    public static async Task<bool> ReportedInlineEntity(SampleContext context, int id) =>
        await context.Products.FirstOrDefaultAsync(candidate => candidate.Id == id) == null;

    // Not reported: the count is also used as a number.
    public static string CountIsUsed(SampleContext context)
    {
        var products = context.Products.Count();
        return products > 0 ? $"{products} products" : "no products";
    }

    // Not reported: the product is used after the null check.
    public static string? EntityIsUsed(SampleContext context, int id)
    {
        var product = context.Products.FirstOrDefault(candidate => candidate.Id == id);
        return product is null ? null : product.Description;
    }

    // Not reported: a nullable column can be null while the row exists.
    public static bool ProjectedColumn(SampleContext context, int id) =>
        context.Products.Where(candidate => candidate.Id == id).Select(candidate => candidate.Description).FirstOrDefault() != null;

    // Not reported: suppressed with a recorded reason.
    public static bool PragmaSuppressed(SampleContext context, int id)
    {
#pragma warning disable EFD002 // Reviewed: the product is loaded so that it is tracked for the rest of the request.
        var product = context.Products.FirstOrDefault(candidate => candidate.Id == id);
#pragma warning restore EFD002
        return product is not null;
    }
}
