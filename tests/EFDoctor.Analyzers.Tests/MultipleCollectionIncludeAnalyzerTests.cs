using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class MultipleCollectionIncludeAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags);");
        yield return Case("static object Run(TestContext context) => context.Set<Parent>().Include(parent => parent.Children).Include(parent => parent.Tags);");
        yield return Case("static object Run(TestContext context) => EntityFrameworkQueryableExtensions.Include(EntityFrameworkQueryableExtensions.Include(context.Parents, parent => parent.Children), parent => parent.Tags);");
        yield return Case("static object Run(TestContext context) => context.Parents.Where(parent => parent.Id > 0).Include(parent => parent.Children).Include(parent => parent.Tags);");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children.Where(child => child.Active)).Include(parent => parent.Tags.OrderBy(tag => tag.Id));");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags).OrderBy(parent => parent.Id);");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags).ToList();");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags).ToListAsync();");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags).Include(parent => parent.Notes);");
        yield return new object[]
        {
            QuerySource("""
                static object Run(TestContext context)
                {
                    var first = context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags);
                    var second = context.Parents.Include(parent => parent.Tags).Include(parent => parent.Notes);
                    return new[] { first, second };
                }
                """),
            2,
        };
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Include(parent => parent.Children);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Include(parent => parent.Category);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Children);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).ThenInclude(child => child.Toys);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags).AsSplitQuery();");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.AsSplitQuery().Include(parent => parent.Children).Include(parent => parent.Tags);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags).AsSingleQuery();");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.AsSingleQuery().Include(parent => parent.Children).Include(parent => parent.Tags).ToList();");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.AsEnumerable().AsQueryable().Include(parent => parent.Children).Include(parent => parent.Tags);");
        yield return SourceCase("static object Run(IQueryable<Parent> query) => query.Include(parent => parent.Children).Include(parent => parent.Tags);");
        yield return SourceCase("static object Run(TestContext context, bool active) { IQueryable<Parent> query = context.Parents.Include(parent => parent.Children); if (active) { query = query.Where(parent => parent.Id > 0); } return query.Include(parent => parent.Tags); }");
        yield return SourceCase("static readonly IQueryable<Parent> Stored = new TestContext().Parents.Include(parent => parent.Children);\nstatic object Run() => Stored.Include(parent => parent.Tags);");
        yield return SourceCase("static object Run(TestContext context) { var query = context.Parents.Include(parent => parent.Children).AsSplitQuery(); return query.Include(parent => parent.Tags).ToList(); }");
        yield return SourceCase("static object Run() => new[] { new Parent() }.AsQueryable().Include(parent => parent.Children).Include(parent => parent.Tags);");
        yield return SourceCase("static object Run(Store store) => store.Include(value => value.Children).Include(value => value.Tags);", "sealed class Store { public Store Include<T>(Func<Parent, T> path) => this; }");
        yield return SourceCase("static object Run(TestContext context) => missing.Include(parent => parent.Children).Include(parent => parent.Tags);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(\"Children\").Include(\"Tags\");");
        yield return SourceCase("static string Run() => \"Include(Children).Include(Tags)\";");
        yield return SourceCase("""
            static object Run(TestContext context)
            {
            #if NEVER
                return context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags);
            #else
                return context.Parents;
            #endif
            }
            """);
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    [Trait("Spec", "efd006-multiple-collection-include/Extension Include chain from DbSet")]
    [Trait("Spec", "efd006-multiple-collection-include/Static Include syntax")]
    [Trait("Spec", "efd006-multiple-collection-include/Two sibling collection navigations")]
    [Trait("Spec", "efd006-multiple-collection-include/Three sibling collection navigations")]
    [Trait("Spec", "efd006-multiple-collection-include/Filtered collection Include")]
    [Trait("Spec", "efd006-multiple-collection-include/Complete structured finding")]
    [Trait("Spec", "efd006-multiple-collection-include/Qualified impact language")]
    [Trait("Spec", "efd006-multiple-collection-include/Safe remediation guidance")]
    [Trait("Spec", "efd006-multiple-collection-include/Analyzer fixture suite")]
    public async Task ReportsEligibleSiblingCollectionIncludes(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
            Assert.Equal(MultipleCollectionIncludeAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
            Assert.Equal("medium", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
            Assert.Equal("EFD006", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
            Assert.Contains("DbSet origin", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
            Assert.Contains("Parent.", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
            Assert.Contains("may multiply", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AsSplitQuery", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    [Trait("Spec", "efd006-multiple-collection-include/In-memory arbitrary or unrelated source")]
    [Trait("Spec", "efd006-multiple-collection-include/Reference plus collection navigation")]
    [Trait("Spec", "efd006-multiple-collection-include/Duplicate collection path")]
    [Trait("Spec", "efd006-multiple-collection-include/Nested ThenInclude path")]
    [Trait("Spec", "efd006-multiple-collection-include/Explicit split query")]
    [Trait("Spec", "efd006-multiple-collection-include/Explicit single query")]
    [Trait("Spec", "efd006-multiple-collection-include/Stored query in a member")]
    [Trait("Spec", "efd006-multiple-collection-include/Explicit client boundary")]
    [Trait("Spec", "efd006-multiple-collection-include/Analyzer fixture suite")]
    public async Task DoesNotReportExcludedShapes(string source)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd006-multiple-collection-include/Stored query")]
    [Trait("Spec", "query-chain-local-tracking/Sibling collection includes split across a local")]
    public async Task SiblingIncludesSplitAcrossALocalAreReportedOnceAtTheLaterInclude()
    {
        var source = QuerySource("""
            static object Run(TestContext context)
            {
                var query = context.Parents.Include(parent => parent.Children);
                return query.Include(parent => parent.Tags).ToList();
            }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("query.Include(parent => parent.Tags)", Text(diagnostic));
        Assert.Contains("Parent.Children, Parent.Tags", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "query-chain-local-tracking/Sibling collection includes before the local")]
    public async Task SiblingIncludesBeforeALocalAreReportedOnceInTheInitializer()
    {
        var source = QuerySource("""
            static object Run(TestContext context)
            {
                var query = context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags);
                return query.Where(parent => parent.Id > 0).OrderBy(parent => parent.Id).ToList();
            }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags)", Text(diagnostic));
    }

    [Fact]
    [Trait("Spec", "efd006-multiple-collection-include/Exact diagnostic location")]
    public async Task ReportsAtSecondDistinctCollectionIncludeAndListsAllPaths()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags).Include(parent => parent.Notes);");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags)", Text(diagnostic));
        Assert.Contains("Parent.Children, Parent.Tags, Parent.Notes", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd006-multiple-collection-include/Standard suppression")]
    public async Task HonorsPragmaSuppression()
    {
        var source = QuerySource("""
            #pragma warning disable EFD006 // Reviewed: split behavior is configured globally.
            static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags);
            #pragma warning restore EFD006
            """);

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd006-multiple-collection-include/Standard suppression")]
    public async Task HonorsEditorConfigSuppression()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags);");

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new MultipleCollectionIncludeAnalyzer(),
            diagnosticId: MultipleCollectionIncludeAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd006-multiple-collection-include/Standard suppression")]
    public async Task HonorsSuppressMessageAttribute()
    {
        var source = QuerySource("""
            [SuppressMessage("Performance", "EFD006", Justification = "Split behavior is configured globally.")]
            static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags);
            """);

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        var source = "// <auto-generated/>\n" + QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Include(parent => parent.Tags);");

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public void DescriptorUsesQualifiedLanguage()
    {
        Assert.Equal("Performance", MultipleCollectionIncludeAnalyzer.Rule.Category);
        Assert.Equal(DiagnosticSeverity.Info, MultipleCollectionIncludeAnalyzer.Rule.DefaultSeverity);
        Assert.Contains("may", MultipleCollectionIncludeAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("will", MultipleCollectionIncludeAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new MultipleCollectionIncludeAnalyzer(),
            diagnosticId: MultipleCollectionIncludeAnalyzer.DiagnosticId);

    private static object[] Case(string member) => [QuerySource(member), 1];

    private static object[] SourceCase(string member, string additionalTypes = "") => [QuerySource(member, additionalTypes)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string QuerySource(string member, string additionalTypes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Child
        {
            public int Id { get; set; }
            public bool Active { get; set; }
            public List<Toy> Toys { get; set; } = new();
        }

        sealed class Tag { public int Id { get; set; } }
        sealed class Note { public int Id { get; set; } }
        sealed class Toy { public int Id { get; set; } }
        sealed class Owner { public int Id { get; set; } }
        sealed class Category { public int Id { get; set; } }

        sealed class Parent
        {
            public int Id { get; set; }
            public Owner Owner { get; set; } = new();
            public Category Category { get; set; } = new();
            public List<Child> Children { get; set; } = new();
            public List<Tag> Tags { get; set; } = new();
            public List<Note> Notes { get; set; } = new();
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Parent> Parents => Set<Parent>();
        }

        {{additionalTypes}}

        static class Subject
        {
            {{member}}
        }
        """;
}
