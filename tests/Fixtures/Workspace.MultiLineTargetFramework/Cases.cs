using Microsoft.EntityFrameworkCore;

namespace Workspace.SingleTarget;

public sealed class Item
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class AppDbContext : DbContext
{
    public DbSet<Item> Items => Set<Item>();
}

public static class Cases
{
    public static void RenameEach(AppDbContext context, IEnumerable<Item> items)
    {
        foreach (var item in items)
        {
            item.Name = item.Name.Trim();
            context.SaveChanges();
        }
    }
}
