using Microsoft.EntityFrameworkCore;

namespace EFD023.Sample;

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class SampleContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
}

public static class SearchCases
{
    public static IQueryable<Customer> ReportedContains(SampleContext context, string term) =>
        context.Customers.Where(customer => customer.Name.Contains(term));

    public static IQueryable<Customer> ReportedEndsWith(SampleContext context, string domain) =>
        context.Customers.Where(customer => customer.Email.EndsWith(domain));

    public static IQueryable<Customer> ReportedLoweredContains(SampleContext context, string term) =>
        context.Customers.Where(customer => customer.Name.ToLower().Contains(term));

    public static IQueryable<Customer> ReportedLeadingWildcardLike(SampleContext context, string term) =>
        context.Customers.Where(customer => EF.Functions.Like(customer.Name, "%" + term));

    public static IQueryable<Customer> PrefixStartsWith(SampleContext context, string prefix) =>
        context.Customers.Where(customer => customer.Name.StartsWith(prefix));

    public static IQueryable<Customer> PrefixLike(SampleContext context, string prefix) =>
        context.Customers.Where(customer => EF.Functions.Like(customer.Name, prefix + "%"));

#pragma warning disable EFD023 // Reviewed: the customer table is small and rarely searched.
    public static IQueryable<Customer> PragmaSuppressed(SampleContext context, string term) =>
        context.Customers.Where(customer => customer.Name.Contains(term));
#pragma warning restore EFD023

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "EFD023",
        Justification = "The customer table is small and rarely searched.")]
    public static IQueryable<Customer> AttributeSuppressed(SampleContext context, string term) =>
        context.Customers.Where(customer => customer.Name.Contains(term));
}
