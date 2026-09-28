using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace EFD022.Sample;

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class SampleContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
}

public static class StringComparisonCases
{
    // Reported: EF Core cannot translate the StringComparison overloads.
    public static IQueryable<Customer> ReportedEquals(SampleContext context, string value) =>
        context.Customers.Where(customer => customer.Name.Equals(value, StringComparison.OrdinalIgnoreCase));

    public static IQueryable<Customer> ReportedStartsWith(SampleContext context, string value) =>
        context.Customers.Where(customer => customer.Name.StartsWith(value, StringComparison.Ordinal));

    public static IQueryable<Customer> ReportedStaticEquals(SampleContext context, string value) =>
        context.Customers.Where(customer => string.Equals(customer.Name, value, StringComparison.Ordinal));

    // Not reported: a plain comparison EF Core can translate.
    public static IQueryable<Customer> Translatable(SampleContext context, string value) =>
        context.Customers.Where(customer => customer.Name == value);

#pragma warning disable EFD022 // Reviewed: this query is materialized and filtered on the client on purpose.
    public static IQueryable<Customer> PragmaSuppressed(SampleContext context, string value) =>
        context.Customers.Where(customer => customer.Name.Equals(value, StringComparison.Ordinal));
#pragma warning restore EFD022

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Correctness",
        "EFD022",
        Justification = "This query is materialized and filtered on the client on purpose.")]
    public static IQueryable<Customer> AttributeSuppressed(SampleContext context, string value) =>
        context.Customers.Where(customer => customer.Name.Equals(value, StringComparison.Ordinal));
}
