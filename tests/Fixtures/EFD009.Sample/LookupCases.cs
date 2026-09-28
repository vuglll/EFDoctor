using Microsoft.EntityFrameworkCore;

namespace EFD009.Sample;

public sealed class Customer
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
}

public sealed class SampleContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
}

public static class LookupCases
{
    public static IQueryable<Customer> ReportedEquality(SampleContext context, string email) =>
        context.Customers.Where(customer => customer.Email.ToLower() == email);

    public static IQueryable<Customer> ReportedStartsWith(SampleContext context, string prefix) =>
        context.Customers.Where(customer => customer.Email.ToUpper().StartsWith(prefix));

    public static IQueryable<Customer> ReportedReversed(SampleContext context, string email) =>
        context.Customers.Where(customer => email == customer.Email.ToLower());

    public static IQueryable<Customer> TransformedValue(SampleContext context, string email) =>
        context.Customers.Where(customer => customer.Email == email.ToLower());

    public static IQueryable<Customer> AlreadyNonSargableContains(SampleContext context, string term) =>
        context.Customers.Where(customer => customer.Email.ToLower().Contains(term));

#pragma warning disable EFD009 // Reviewed: an expression index on LOWER(Email) backs this lookup.
    public static IQueryable<Customer> PragmaSuppressed(SampleContext context, string email) =>
        context.Customers.Where(customer => customer.Email.ToLower() == email);
#pragma warning restore EFD009

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "EFD009",
        Justification = "An expression index on LOWER(Email) backs this lookup.")]
    public static IQueryable<Customer> AttributeSuppressed(SampleContext context, string email) =>
        context.Customers.Where(customer => customer.Email.ToLower() == email);
}
