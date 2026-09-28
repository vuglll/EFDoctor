using Microsoft.EntityFrameworkCore;

namespace EFD005.Sample;

[Index(nameof(ExternalKey), IsUnique = true)]
public sealed class Customer
{
    public int Id { get; set; }
    [System.ComponentModel.DataAnnotations.Key]
    public int MetadataKey { get; set; }
    public int ProductId { get; set; }
    public string ExternalKey { get; set; } = "";
    public string? Name { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<Customer> Children { get; set; } = [];
}

public sealed class SampleContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
}

public static class MaterializationCases
{
    public static List<Customer> ReportedFullSet(SampleContext context)
    {
        return context.Customers.ToList();
    }

    public static Task<List<Customer>> ReportedFilteredAsync(SampleContext context)
    {
        return context.Customers.Where(customer => customer.Active).ToListAsync();
    }

    public static List<Customer> NameHeuristicBound(SampleContext context, int productId) =>
        context.Customers.Where(customer => customer.ProductId == productId).ToList();

    public static List<Customer> TimeWindowBound(SampleContext context, DateTime since) =>
        context.Customers.Where(customer => customer.CreatedUtc >= since).ToList();

    public static List<Customer> BoundedByTake(SampleContext context, int limit) =>
        context.Customers.OrderBy(customer => customer.Id).Take(limit).ToList();

    public static List<Customer> BoundedByMembership(SampleContext context, int[] productIds) =>
        context.Customers.Where(customer => productIds.Contains(customer.ProductId)).ToList();

    public static List<Customer> BoundedByAny(SampleContext context, int[] productIds) =>
        context.Customers.Where(customer => productIds.Any(id => id == customer.ProductId)).ToList();

    public static List<Customer> BoundedByMetadataKey(SampleContext context, int key) =>
        context.Customers.Where(customer => customer.MetadataKey == key).ToList();

    public static IEnumerable<Customer> Efd004Overlap(SampleContext context) =>
        context.Customers.ToList().Where(customer => customer.Active);

    public static async Task<object[]> ProductionFiveQueryShape(
        SampleContext context,
        int[] productIds)
    {
        var first = await context.Customers
            .Where(customer => productIds.Contains(customer.ProductId))
            .ToListAsync();
        var second = await context.Customers
            .Where(customer => productIds.Contains(customer.ProductId))
            .AsSplitQuery()
            .ToListAsync();
        var third = await context.Customers
            .Where(customer => productIds.Contains(customer.ProductId))
            .Include(customer => customer.Children)
            .ThenInclude(child => child.Children)
            .AsNoTrackingWithIdentityResolution()
            .TagWith("hydrate children")
            .ToListAsync();
        var fourthQuery = context.Customers
            .Where(customer => productIds.Contains(customer.ProductId));
        var fourth = await fourthQuery.ToListAsync();
        var fifth = await context.Customers
            .Where(customer => productIds.Contains(customer.ProductId))
            .AsSingleQuery()
            .ToListAsync();
        return [first, second, third, fourth, fifth];
    }

#pragma warning disable EFD005 // Reviewed: intentional full export processed by a controlled batch.
    public static List<Customer> PragmaSuppressed(SampleContext context) =>
        context.Customers.ToList();
#pragma warning restore EFD005

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "EFD005",
        Justification = "Known-small lookup table is intentionally cached in full.")]
    public static List<Customer> AttributeSuppressed(SampleContext context) =>
        context.Customers.AsNoTracking().ToList();
}

public sealed class CustomerService
{
    public List<Customer> LoadAll(SampleContext context) =>
        context.Customers.ToList();
}
