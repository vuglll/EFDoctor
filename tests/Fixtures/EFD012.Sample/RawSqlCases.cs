using Microsoft.EntityFrameworkCore;

namespace EFD012.Sample;

public sealed class Entity
{
    public int Id { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<Entity> Entities => Set<Entity>();
}

public static class RawSqlCases
{
    public static IQueryable<Entity> ReportedInterpolation(SampleContext context, int id) =>
        context.Entities.FromSqlRaw($"SELECT * FROM Entities WHERE Id = {id}");

    public static int ReportedLocalConcatenation(SampleContext context, string name)
    {
        var sql = "DELETE FROM Entities WHERE Name = '" + name + "'";
        return context.Database.ExecuteSqlRaw(sql);
    }

    public static IQueryable<Entity> SafeParameterized(SampleContext context, int id) =>
        context.Entities.FromSqlRaw("SELECT * FROM Entities WHERE Id = {0}", id);

    public static IQueryable<Entity> SafeInterpolated(SampleContext context, int id) =>
        context.Entities.FromSqlInterpolated($"SELECT * FROM Entities WHERE Id = {id}");

#pragma warning disable EFD012 // Reviewed: fragment is selected from a fixed allow-list.
    public static int PragmaSuppressed(SampleContext context, string fragment) =>
        context.Database.ExecuteSqlRaw("DELETE FROM Entities " + fragment);
#pragma warning restore EFD012

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "EFD012",
        Justification = "Fragment is selected from a fixed allow-list.")]
    public static int AttributeSuppressed(SampleContext context, string fragment) =>
        context.Database.ExecuteSqlRaw($"DELETE FROM Entities {fragment}");
}
