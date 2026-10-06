using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class BulkUpdateDeleteAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Positive("var rows = context.Entities.Where(e => e.Id > 10).ToList(); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();");
        yield return Positive("foreach (var entity in context.Entities.Where(e => e.Id > 10).ToList()) { entity.Active = false; } context.SaveChanges();");
        yield return Positive("var rows = context.Entities.Where(e => e.Id > 10).ToArray(); foreach (var entity in rows) entity.Active = false; context.SaveChanges();");
        yield return Positive("var rows = await context.Entities.Where(e => e.Id > 10).ToListAsync(); foreach (var entity in rows) { entity.Active = false; } await context.SaveChangesAsync();", asynchronous: true);
        yield return Positive("var rows = await context.Entities.Where(e => e.Id > 10).ToArrayAsync(); foreach (var entity in rows) { entity.Active = false; } await context.SaveChangesAsync();", asynchronous: true);
        yield return Positive("var rows = context.Entities.Where(e => e.Id > 10).ToList(); foreach (var entity in rows) { entity.Active = false; entity.Status = 2; } context.SaveChanges();");
        yield return Positive("var rows = context.Entities.Where(e => e.Id > 10).ToList(); foreach (var entity in rows) { entity.Status = status; } context.SaveChanges();", parameters: ", int status");
        yield return Positive("var status = 3; var rows = context.Entities.Where(e => e.Id > 10).ToList(); foreach (var entity in rows) { entity.Status = status; } context.SaveChanges();");
        yield return Positive("var rows = context.Entities.Where(e => e.Id > 10).ToList(); foreach (var entity in rows) { entity.Status = Constants.Status; } context.SaveChanges();", additionalTypes: "static class Constants { public static readonly int Status = 4; }");
        yield return Positive("var rows = context.Set<Entity>().Where(e => e.Id > 10).ToList(); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();");
        yield return Positive("var rows = context.Entities.Where(e => e.Id > 10).ToList(); foreach (var entity in rows) { context.Remove(entity); } context.SaveChanges();");
        yield return Positive("var rows = context.Entities.Where(e => e.Id > 10).ToList(); foreach (var entity in rows) { context.Entities.Remove(entity); } context.SaveChanges();");
        yield return Positive("var rows = await context.Entities.Where(e => e.Id > 10).ToListAsync(); foreach (var entity in rows) { context.Remove(entity); } await context.SaveChangesAsync();", asynchronous: true);
        yield return Positive(
            "var rows = context.Entities.Where(e => e.Id > 10).ToList(); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();",
            contextType: "OverridingContext",
            additionalTypes: "sealed class OverridingContext : TestContext { public override int SaveChanges(bool acceptAllChangesOnSuccess) => base.SaveChanges(acceptAllChangesOnSuccess); public override int SaveChanges() => base.SaveChanges(); }");
        yield return Positive("var query = context.Entities.Where(e => e.Id > 10); var rows = query.ToList(); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Status = entity.Status + 1; } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Status = GetStatus(); } context.SaveChanges();", additionalMembers: "static int GetStatus() => 1;");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { if (entity.Id > 0) entity.Active = false; } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Active = false; Console.WriteLine(entity.Id); } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Active = false; context.SaveChanges(); }");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Active = false; } Console.WriteLine(rows.Count); context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Active = false; } other.SaveChanges();", parameters: ", TestContext other");
        yield return Negative("var rows = context.Entities.ToList(); rows = context.Entities.Where(e => e.Id > 2).ToList(); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); Console.WriteLine(rows.Count); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();");
        yield return Negative("var rows = new List<Entity>(); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Owned.Value = 1; } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Status += 1; } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Status++; } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Status = 1; entity.Status = 2; } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Active = false; context.Remove(entity); } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { context.RemoveRange(entity); } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); var other = new Entity(); foreach (var entity in rows) { context.Remove(other); } context.SaveChanges();");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Active = false; } context.SaveChangesAsync();");
        yield return Negative("var rows = await context.Entities.ToListAsync(); foreach (var entity in rows) { entity.Active = false; } var save = context.SaveChangesAsync(); await save;", asynchronous: true);
        yield return Negative("var rows = context.Entities.ToList(); Touch(ref rows); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();", additionalMembers: "static void Touch(ref List<Entity> rows) { }");
        yield return Negative("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Active = false; } SaveChanges();", additionalMembers: "static void SaveChanges() { }");
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsPositiveCases(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Count(static diagnostic => diagnostic.Id == BulkUpdateDeleteAnalyzer.DiagnosticId));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportNegativeCases(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Count(static diagnostic => diagnostic.Id == BulkUpdateDeleteAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task RequiresBulkOperationApis()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext { public virtual int SaveChanges() => 0; }
                public class DbSet<T> : List<T> { public T Remove(T entity) => entity; }
                public static class EntityFrameworkQueryableExtensions { }
            }

            sealed class Entity { public int Id { get; set; } }
            sealed class TestContext : Microsoft.EntityFrameworkCore.DbContext
            {
                public Microsoft.EntityFrameworkCore.DbSet<Entity> Entities { get; } = new();
            }

            static class Subject
            {
                static void Run(TestContext context)
                {
                    var rows = context.Entities.ToList();
                    foreach (var entity in rows) { entity.Id = 1; }
                    context.SaveChanges();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeEntityFramework: false,
            analyzer: new BulkUpdateDeleteAnalyzer(),
            diagnosticId: BulkUpdateDeleteAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsUpdateAtMaterializerWithActionableProperties()
    {
        var source = CaseSource("var rows = context.Entities.Where(e => e.Id > 10).ToList(); foreach (var entity in rows) { entity.Active = false; entity.Status = 2; } context.SaveChanges();");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        var text = await diagnostic.Location.SourceTree!.GetTextAsync();

        Assert.Equal("context.Entities.Where(e => e.Id > 10).ToList()", text.ToString(diagnostic.Location.SourceSpan));
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal(BulkUpdateDeleteAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("medium", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains("uniform assignments to Active, Status", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("per-row", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact], StringComparison.Ordinal);
        Assert.Contains("ExecuteUpdate", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("tracked-entity state", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("concurrency-token", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("interceptors", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("domain callbacks", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("cascade", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("transaction", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Equal("EFD013", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
    }

    [Fact]
    public async Task ReportsDeleteWithDeleteSpecificEvidenceAndRemediation()
    {
        var source = CaseSource("var rows = context.Entities.Where(e => e.Id > 10).ToList(); foreach (var entity in rows) { context.Remove(entity); } context.SaveChanges();");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Contains("load-delete-save", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("delete", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ExecuteDelete", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    public async Task HonorsPragmaSuppression()
    {
        var source = CaseSource("#pragma warning disable EFD013\nvar rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();\n#pragma warning restore EFD013");

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task HonorsEditorConfigurationSuppression()
    {
        var source = CaseSource("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();");

        Assert.Empty(await AnalyzeAsync(source, ReportDiagnostic.Suppress));
    }

    [Fact]
    public async Task HonorsSuppressMessageAttribute()
    {
        var source = CaseSource(
            "var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();",
            methodAttribute: "[SuppressMessage(\"Performance\", \"EFD013\", Justification = \"Tracked callbacks are required.\")]");

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task IgnoresGeneratedSource()
    {
        var source = "// <auto-generated />\n" + CaseSource("var rows = context.Entities.ToList(); foreach (var entity in rows) { entity.Active = false; } context.SaveChanges();");

        Assert.Empty(await AnalyzeAsync(source));
    }

    private static object[] Positive(
        string body,
        bool asynchronous = false,
        string parameters = "",
        string contextType = "TestContext",
        string additionalTypes = "") =>
        new object[] { CaseSource(body, asynchronous, parameters, contextType, additionalTypes), 1 };

    private static object[] Negative(
        string body,
        bool asynchronous = false,
        string parameters = "",
        string additionalMembers = "") =>
        new object[] { CaseSource(body, asynchronous, parameters, additionalMembers: additionalMembers), 0 };

    private static string CaseSource(
        string body,
        bool asynchronous = false,
        string parameters = "",
        string contextType = "TestContext",
        string additionalTypes = "",
        string additionalMembers = "",
        string methodAttribute = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        class TestContext : DbContext
        {
            public DbSet<Entity> Entities => Set<Entity>();
        }

        sealed class Entity
        {
            public int Id { get; set; }
            public int Status { get; set; }
            public bool Active { get; set; }
            public Owned Owned { get; set; } = new();
        }

        sealed class Owned { public int Value { get; set; } }
        {{additionalTypes}}

        static class Subject
        {
            {{methodAttribute}}
            static {{(asynchronous ? "async Task" : "void")}} Run({{contextType}} context{{parameters}})
            {
                {{body}}
            }

            {{additionalMembers}}
        }
        """;

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        ReportDiagnostic? configuredSeverity = null) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            configuredSeverity: configuredSeverity,
            analyzer: new BulkUpdateDeleteAnalyzer(),
            diagnosticId: BulkUpdateDeleteAnalyzer.DiagnosticId);
}
