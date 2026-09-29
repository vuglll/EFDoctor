using Microsoft.EntityFrameworkCore;

namespace EFD005.TestSample;

public sealed class TestEntity
{
    public int Id { get; set; }
    public bool Active { get; set; }
    public string Name { get; set; } = "";
    public TestEntity? Parent { get; set; }
}

public sealed class TestContext : DbContext
{
    public DbSet<TestEntity> Entities => Set<TestEntity>();
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
}
