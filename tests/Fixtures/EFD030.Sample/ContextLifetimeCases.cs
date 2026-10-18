using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD030.Sample;

public sealed class Order
{
    public int Id { get; set; }
    public bool Open { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class ContextLifetimeCases(IDbContextFactory<SampleContext> factory, SampleContext injected)
{
    // Reported: the context is disposed at the return, before the caller runs the query.
    public IQueryable<Order> ReportedUsingDeclaration()
    {
        using var db = factory.CreateDbContext();
        return db.Orders.Where(order => order.Open);
    }

    // Reported: the same with a using statement.
    public IQueryable<Order> ReportedUsingStatement()
    {
        using (var db = factory.CreateDbContext())
        {
            return db.Orders.OrderBy(order => order.Id);
        }
    }

    // Not reported: the result is materialized while the context is alive.
    public async Task<List<Order>> Materialized()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Orders.Where(order => order.Open).Take(50).ToListAsync();
    }

    // Not reported: the injected context outlives this method.
    public IQueryable<Order> InjectedContext() => injected.Orders.Where(order => order.Open);

    // Not reported: suppressed with a recorded reason.
    [SuppressMessage("Reliability", "EFD030", Justification = "Test helper; the caller enumerates inside the scope.")]
    public IQueryable<Order> Suppressed()
    {
        using var db = factory.CreateDbContext();
        return db.Orders;
    }
}
