using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD038.Sample;

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool Discontinued { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
}

public static class StaleEntityCases
{
    // Reported (high): FindAsync returns the tracked instance, which still has the old price.
    public static async Task<decimal> ReportedReloadAfterUpdate(SampleContext context, int id, decimal newPrice)
    {
        var product = await context.Products.FindAsync(id);
        await context.Products.Where(candidate => candidate.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Price, newPrice));
        var again = await context.Products.FindAsync(id);
        return again!.Price;
    }

    // Reported (medium): the tracked products are stale, but this method doesn't use them again.
    public static void ReportedLeftStale(SampleContext context)
    {
        var products = context.Products.Where(candidate => candidate.Discontinued).Take(100).ToList();
        Console.WriteLine(products.Count);
        context.Products.Where(candidate => candidate.Discontinued).ExecuteDelete();
    }

    // Not reported: the bulk operation runs first, so the load reads current values.
    public static async Task<Product?> BulkOperationFirst(SampleContext context, int id, decimal newPrice)
    {
        await context.Products.Where(candidate => candidate.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Price, newPrice));
        var product = await context.Products.FindAsync(id);
        return product;
    }

    // Not reported: the entity isn't tracked.
    public static async Task<string> NoTrackingLoad(SampleContext context, int id)
    {
        var product = await context.Products.AsNoTracking().FirstAsync(candidate => candidate.Id == id);
        await context.Products.Where(candidate => candidate.Id == id).ExecuteDeleteAsync();
        return product.Name;
    }

    // Not reported: the tracker is cleared after the bulk operation.
    public static async Task<Product?> TrackerCleared(SampleContext context, int id, decimal newPrice)
    {
        var product = await context.Products.FindAsync(id);
        await context.Products.Where(candidate => candidate.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Price, newPrice));
        context.ChangeTracker.Clear();
        return await context.Products.FindAsync(id);
    }

    // Not reported: suppressed with a recorded reason.
    public static async Task<Product?> PragmaSuppressed(SampleContext context, int id)
    {
        var product = await context.Products.FindAsync(id);
#pragma warning disable EFD038 // Reviewed: the cleanup filter can't match the loaded product.
        await context.Products.Where(candidate => candidate.Discontinued).ExecuteDeleteAsync();
#pragma warning restore EFD038
        return product;
    }

    [SuppressMessage("Correctness", "EFD038", Justification = "The cleanup filter can't match the loaded product.")]
    public static async Task<Product?> AttributeSuppressed(SampleContext context, int id)
    {
        var product = await context.Products.FindAsync(id);
        await context.Products.Where(candidate => candidate.Discontinued).ExecuteDeleteAsync();
        return product;
    }
}
