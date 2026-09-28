using Microsoft.EntityFrameworkCore;

namespace EFD019.Sample;

public sealed class Invoice
{
    public int Id { get; set; }
    public bool Paid { get; set; }
    public decimal Amount { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
}

public static class ReductionCases
{
    public static Invoice? ReportedElement(SampleContext context, int id) =>
        context.Invoices.ToList().FirstOrDefault(invoice => invoice.Id == id);

    public static async Task<int> ReportedAsyncCount(SampleContext context) =>
        (await context.Invoices.Where(invoice => !invoice.Paid).ToListAsync()).Count;

    public static decimal ReportedAggregate(SampleContext context) =>
        context.Invoices.ToArray().Sum(invoice => invoice.Amount);

    public static Task<int> QuerySide(SampleContext context) =>
        context.Invoices.CountAsync();

    public static Invoice Stored(SampleContext context)
    {
        var invoices = context.Invoices.ToList();
        return invoices.First();
    }

    public static Invoice ClientBoundary(SampleContext context) =>
        context.Invoices.AsEnumerable().First();

    public static Invoice UnorderedLast(SampleContext context) =>
        context.Invoices.ToList().Last();

#pragma warning disable EFD019 // Reviewed: the table holds a handful of configuration rows.
    public static bool PragmaSuppressed(SampleContext context) =>
        context.Invoices.ToList().Any();
#pragma warning restore EFD019

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "EFD019",
        Justification = "The table holds a handful of configuration rows.")]
    public static int AttributeSuppressed(SampleContext context) =>
        context.Invoices.ToArray().Length;
}
