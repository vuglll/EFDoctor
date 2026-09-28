using Microsoft.EntityFrameworkCore;

namespace EFD018.Sample;

public sealed class AuditEntry
{
    public int Id { get; set; }
    public bool Archived { get; set; }
}

public sealed class SampleContext : DbContext
{
    public DbSet<AuditEntry> Entries => Set<AuditEntry>();
}

public static class DiscardCases
{
    public static async Task ReportedStatement(SampleContext context, AuditEntry entry)
    {
        context.Entries.Add(entry);
        context.SaveChangesAsync();
        await Task.Yield();
    }

    public static void ReportedDiscard(SampleContext context) =>
        _ = context.Entries.Where(entry => entry.Archived).ExecuteDeleteAsync();

    public static async Task<int> Awaited(SampleContext context) =>
        await context.SaveChangesAsync();

    public static Task<int> Returned(SampleContext context) =>
        context.SaveChangesAsync();

    public static int Blocked(SampleContext context) =>
        context.SaveChangesAsync().Result;

    public static void PragmaSuppressed(SampleContext context)
    {
#pragma warning disable EFD018 // Reviewed: best-effort audit write on a dedicated, separately scoped context.
        _ = context.SaveChangesAsync();
#pragma warning restore EFD018
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Correctness",
        "EFD018",
        Justification = "Best-effort audit write on a dedicated, separately scoped context.")]
    public static void AttributeSuppressed(SampleContext context) =>
        _ = context.SaveChangesAsync();
}
