using Microsoft.EntityFrameworkCore;

namespace Workspace.SplitQueryData;

public sealed class Order
{
    public int Id { get; set; }

    public List<OrderLine> Lines { get; set; } = [];

    public List<Payment> Payments { get; set; } = [];
}

public sealed class OrderLine
{
    public int Id { get; set; }
}

public sealed class Payment
{
    public int Id { get; set; }
}

public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class Orders
{
    public static Task<Order?> FindAsync(ShopContext context, int id) =>
        context.Orders
            .Include(order => order.Lines)
            .Include(order => order.Payments)
            .FirstOrDefaultAsync(order => order.Id == id);
}
