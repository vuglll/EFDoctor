using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EFD034.Sample;

public sealed class ShopContext : DbContext
{
}

public sealed class CacheContext : DbContext
{
}

[DbContext(typeof(ShopContext))]
[Migration("20260101000000_Initial")]
public sealed class Initial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }
}

public static class DatabaseSetupCases
{
    // Reported: ShopContext has migrations, which EnsureCreated bypasses.
    public static void ReportedStartup(ShopContext context) => context.Database.EnsureCreated();

    // Reported: the async form.
    public static async Task ReportedStartupAsync(ShopContext context) => await context.Database.EnsureCreatedAsync();

    // Not reported: Migrate applies the migrations.
    public static void Upgrade(ShopContext context) => context.Database.Migrate();

    // Not reported: CacheContext has no migrations.
    public static void CreateCache(CacheContext cache) => cache.Database.EnsureCreated();

    // Not reported: suppressed with a recorded reason.
    [SuppressMessage("Correctness", "EFD034", Justification = "Local demo database.")]
    public static void Demo(ShopContext context) => context.Database.EnsureCreated();
}
