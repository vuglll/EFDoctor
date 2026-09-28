using Microsoft.EntityFrameworkCore;

namespace EFD021.Sample;

public sealed class AppDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class Order
{
    public int Id { get; set; }
}

public static class DbContextFieldCases
{
    // Reported: a DbContext held in static, process-lifetime state.
    public static AppDbContext Shared = null!;
}

public sealed class InjectedRepository
{
    // Not reported: an instance field is per-repository, scoped by the container.
    private readonly AppDbContext _context;

    public InjectedRepository(AppDbContext context) => _context = context;

    public Order? Find(int id) => _context.Orders.Find(id);
}

public static class SuppressedCases
{
#pragma warning disable EFD021 // Reviewed: single-threaded console tool that disposes the context on exit.
    public static AppDbContext PragmaSuppressed = null!;
#pragma warning restore EFD021

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "EFD021",
        Justification = "Single-threaded console tool that disposes the context on exit.")]
    public static AppDbContext AttributeSuppressed = null!;
}
