using Microsoft.EntityFrameworkCore;

namespace EFD027.Sample;

public sealed class Order
{
    public int Id { get; set; }
}

public sealed class Customer
{
    public int Id { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Customer> Customers => Set<Customer>();
}

public sealed class DashboardService(SampleContext context, IDbContextFactory<SampleContext> factory)
{
    // Reported (high): both queries start before either completes, on the same injected context.
    public Task LoadTogether() =>
        Task.WhenAll(context.Orders.CountAsync(), context.Customers.AnyAsync());

    // Reported (high): the count starts while the orders task local is still pending.
    public async Task<int> LoadWithPendingTask()
    {
        var orders = context.Orders.CountAsync();
        var customers = await context.Customers.CountAsync();
        return await orders + customers;
    }

    // Reported (high): every selector invocation starts FindAsync on the captured context.
    public Task LoadEach(int[] ids) =>
        Task.WhenAll(ids.Select(async id => await context.Orders.FindAsync(id)));

    // Not reported: each operation is awaited before the next one starts.
    public async Task LoadSequentially()
    {
        await context.Orders.CountAsync();
        await context.Customers.AnyAsync();
    }

    // Not reported: each concurrent operation gets its own context.
    public async Task LoadInParallel()
    {
        await using var first = factory.CreateDbContext();
        await using var second = factory.CreateDbContext();
        await Task.WhenAll(first.Orders.CountAsync(), second.Customers.AnyAsync());
    }

#pragma warning disable EFD027 // Reviewed: the context in this test is a thread-safe double.
    public Task PragmaSuppressed() =>
        Task.WhenAll(context.Orders.CountAsync(), context.Customers.AnyAsync());
#pragma warning restore EFD027

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "EFD027",
        Justification = "The context in this test is a thread-safe double.")]
    public Task AttributeSuppressed() =>
        Task.WhenAll(context.Orders.CountAsync(), context.Customers.AnyAsync());
}
