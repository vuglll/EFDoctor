using Microsoft.EntityFrameworkCore;
using Workspace.SplitQueryData;

namespace Workspace.SplitQueryHost;

public static class ContextOptions
{
    public static DbContextOptions<ShopContext> Create(string connectionString) =>
        new DbContextOptionsBuilder<ShopContext>()
            .UseSqlServer(connectionString, sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .Options;
}
