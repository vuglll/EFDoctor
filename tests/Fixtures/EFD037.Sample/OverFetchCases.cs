using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD037.Sample;

public sealed class Invoice
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Number { get; set; } = "";
    public decimal Total { get; set; }
    public string Currency { get; set; } = "";
    public DateTime IssuedUtc { get; set; }
    public DateTime? PaidUtc { get; set; }
    public string? Notes { get; set; }
    public Customer? Customer { get; set; }
}

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed record InvoiceSummary(int Id, decimal Total);

public sealed class SampleContext : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
}

public static class OverFetchCases
{
    // Reported (advisory): eight columns are loaded for each invoice, and two are read.
    public static async Task<List<InvoiceSummary>> ReportedProjection(SampleContext context, int customerId)
    {
        var invoices = await context.Invoices.Where(invoice => invoice.CustomerId == customerId).ToListAsync();
        return invoices.Select(invoice => new InvoiceSummary(invoice.Id, invoice.Total)).ToList();
    }

    // Reported (advisory): the loop reads three of the eight columns.
    public static decimal ReportedLoop(SampleContext context)
    {
        var invoices = context.Invoices.AsNoTracking().OrderBy(invoice => invoice.Id).Take(100).ToList();
        var total = 0m;
        foreach (var invoice in invoices)
        {
            if (invoice.PaidUtc is null)
            {
                Console.WriteLine(invoice.Number);
                total += invoice.Total;
            }
        }

        return total;
    }

    // Not reported: the projection already runs in SQL.
    public static Task<List<InvoiceSummary>> ProjectedInTheQuery(SampleContext context, int customerId) =>
        context.Invoices
            .Where(invoice => invoice.CustomerId == customerId)
            .Select(invoice => new InvoiceSummary(invoice.Id, invoice.Total))
            .ToListAsync();

    // Not reported: the entities are modified and saved, so they are needed whole.
    public static void ModifiedAndSaved(SampleContext context)
    {
        var invoices = context.Invoices.Where(invoice => invoice.PaidUtc == null).Take(100).ToList();
        foreach (var invoice in invoices)
        {
            invoice.Notes = "Reminder sent for " + invoice.Number;
        }

        context.SaveChanges();
    }

    // Not reported: the entities leave the method.
    public static List<Invoice> Returned(SampleContext context)
    {
        var invoices = context.Invoices.Take(50).ToList();
        Console.WriteLine(invoices.Count(invoice => invoice.Total > 0));
        return invoices;
    }

    // Not reported: the customer navigation is used.
    public static List<string> NavigationRead(SampleContext context)
    {
        var invoices = context.Invoices.Include(invoice => invoice.Customer).Take(50).ToList();
        return invoices.Select(invoice => invoice.Customer!.Name).ToList();
    }

    // Not reported: suppressed with a recorded reason.
    public static List<int> PragmaSuppressed(SampleContext context)
    {
#pragma warning disable EFD037 // Reviewed: the invoice table is narrow in production.
        var invoices = context.Invoices.Take(50).ToList();
#pragma warning restore EFD037
        return invoices.Select(invoice => invoice.Id).ToList();
    }

    [SuppressMessage("Performance", "EFD037", Justification = "The invoice table is narrow in production.")]
    public static List<int> AttributeSuppressed(SampleContext context)
    {
        var invoices = context.Invoices.Take(50).ToList();
        return invoices.Select(invoice => invoice.Id).ToList();
    }
}
