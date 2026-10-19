using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD016.Sample;

public sealed class Order
{
    public int Id { get; set; }
    public bool Open { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class TokenCases
{
    // Reported: the query keeps running if the caller cancels.
    public static Task<List<Order>> ReportedQuery(SampleContext context, CancellationToken cancellationToken) =>
        context.Orders.Where(order => order.Open).Take(50).ToListAsync();

    // Reported: the save ignores the token too.
    public static Task<int> ReportedSave(SampleContext context, CancellationToken cancellationToken) =>
        context.SaveChangesAsync();

    // Not reported: the token is passed.
    public static Task<int> Passed(SampleContext context, CancellationToken cancellationToken) =>
        context.Orders.CountAsync(cancellationToken);

    // Not reported: CancellationToken.None is an explicit choice.
    public static Task<int> Declined(SampleContext context, CancellationToken cancellationToken) =>
        context.SaveChangesAsync(CancellationToken.None);

    // Not reported: suppressed with a recorded reason.
    [SuppressMessage("Reliability", "EFD016", Justification = "Must finish even if the caller cancels.")]
    public static Task<int> Suppressed(SampleContext context, CancellationToken cancellationToken) =>
        context.SaveChangesAsync();
}
