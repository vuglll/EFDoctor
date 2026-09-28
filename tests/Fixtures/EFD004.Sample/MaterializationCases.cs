using Microsoft.EntityFrameworkCore;

namespace EFD004.Sample;

public sealed class Customer
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool Active { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
}

public static class MaterializationCases
{
    public static IEnumerable<Customer> ReportedFilter(SampleContext context, int minimumId)
    {
        return context.Customers.ToList().Where(customer => customer.Id >= minimumId);
    }

    public static async Task<IEnumerable<string?>> ReportedProjectionAsync(SampleContext context)
    {
        return (await context.Customers.ToArrayAsync()).Select(customer => customer.Name);
    }

#pragma warning disable EFD004 // Reviewed: this bounded result is intentionally reused by client-only processing.
    public static IEnumerable<Customer> PragmaSuppressed(SampleContext context) =>
        context.Customers.ToList().Take(5);
#pragma warning restore EFD004

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "EFD004",
        Justification = "A provider limitation requires intentional client-side ordering for this bounded set.")]
    public static IEnumerable<Customer> AttributeSuppressed(SampleContext context) =>
        context.Customers.ToArray().OrderBy(customer => customer.Name);
}
