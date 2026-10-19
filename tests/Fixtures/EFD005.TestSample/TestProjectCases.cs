using Microsoft.EntityFrameworkCore;

namespace EFD005.TestSample;

public sealed class TestEntity
{
    public int Id { get; set; }
    public bool Active { get; set; }
    public string Name { get; set; } = "";
    public TestEntity? Parent { get; set; }
}

public sealed class WideEntity
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

public sealed class TestContext : DbContext
{
    public DbSet<TestEntity> Entities => Set<TestEntity>();

    public DbSet<WideEntity> Wide => Set<WideEntity>();
}

public static class TestProjectCases
{
    public static List<TestEntity> Materialize(TestContext context) =>
        context.Entities.ToList();

    public static void SaveInLoop(TestContext context, IEnumerable<TestEntity> entities)
    {
        foreach (var entity in entities)
        {
            context.Update(entity);
            context.SaveChanges();
        }
    }

    public static int UnsafeRawSql(TestContext context, int id) =>
        context.Database.ExecuteSqlRaw($"DELETE FROM TestEntities WHERE Id = {id}");

    public static void BulkUpdateCandidate(TestContext context)
    {
        var rows = context.Entities.Where(entity => entity.Id > 0).Take(10).ToList();
        foreach (var entity in rows)
        {
            entity.Active = false;
        }

        context.SaveChanges();
    }

    public static IQueryable<int> ProjectionDropsInclude(TestContext context) =>
        context.Entities.Include(entity => entity.Parent).Select(entity => entity.Id);

    public static void DiscardedSave(TestContext context) =>
        _ = context.SaveChangesAsync();

    public static int CountAfterMaterialization(TestContext context) =>
        context.Entities.ToArray().Length;

    public static IQueryable<TestEntity> RedundantInclude(TestContext context) =>
        context.Entities.Include(entity => entity.Parent).Include(entity => entity.Parent);

    public static IQueryable<TestEntity> CaseTransformedLookup(TestContext context, string name) =>
        context.Entities.Where(entity => entity.Name.ToLower() == name);

    public static IQueryable<TestEntity> SubstringSearch(TestContext context, string term) =>
        context.Entities.Where(entity => entity.Name.Contains(term));

    public static IQueryable<TestEntity> ReplacedOrdering(TestContext context) =>
        context.Entities.OrderBy(entity => entity.Name).OrderBy(entity => entity.Id);

    public static Task ConcurrentQueries(TestContext context) =>
        Task.WhenAll(context.Entities.AnyAsync(), context.Entities.CountAsync());

    public static List<int> OverFetch(TestContext context)
    {
        var rows = context.Wide.Take(10).ToList();
        return rows.Select(row => row.Id).ToList();
    }

    public static void StaleAfterBulkDelete(TestContext context, int id)
    {
        var entity = context.Entities.Find(id);
        context.Entities.Where(candidate => candidate.Id == id).ExecuteDelete();
    }

    public static IQueryable<TestEntity> ClientMethodFilter(TestContext context) =>
        context.Entities.Where(entity => HasName(entity));

    private static bool HasName(TestEntity entity) => entity.Name.Length > 0;

    public static IQueryable<TestEntity> QueryOutlivesContext()
    {
        using var context = new TestContext();
        return context.Entities;
    }

    public static void UntrackedChange(TestContext context, int id)
    {
        var entity = context.Entities.AsNoTracking().First(candidate => candidate.Id == id);
        entity.Name = "renamed";
        context.SaveChanges();
    }

    public static void SingletonContext(Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<ContextHolder>(services);

    public static TestEntity? ProcedureFirst(TestContext context) =>
        context.Entities.FromSqlRaw("EXEC dbo.GetEntities").FirstOrDefault();

    public static List<TestEntity> MaterializedContains(TestContext context)
    {
        var ids = context.Entities.Where(entity => entity.Active).Select(entity => entity.Id).ToList();
        return context.Entities.Where(entity => ids.Contains(entity.Id)).Take(10).ToList();
    }

    public static void CreateAndMigrate(TestContext context)
    {
        context.Database.EnsureCreated();
        context.Database.Migrate();
    }
}

public sealed class ContextHolder(TestContext context)
{
    public TestContext Context { get; } = context;
}
