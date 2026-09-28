using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class ProjectionDropsIncludeAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id);");
        yield return Case("static object Run(TestContext context) => context.Set<Parent>().Include(parent => parent.Owner).Select(parent => parent.Name);");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => new ParentDto { Id = parent.Id, OwnerName = parent.Owner.Name });");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => new ParentSummary(parent.Id, parent.Name));");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Select(parent => new { parent.Id, parent.Name });");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Select(parent => (parent.Id, parent.Name));");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Include(parent => parent.Children).Select(parent => parent.Id);");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).ThenInclude(child => child.Toys).Select(parent => parent.Children.Count());");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children.Where(child => child.Active).OrderBy(child => child.Id)).Select(parent => parent.Id);");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Where(parent => parent.Id > 0).OrderBy(parent => parent.Name).ThenBy(parent => parent.Id).AsNoTracking().TagWith(\"report\").Skip(1).Take(5).Select(parent => parent.Id);");
        yield return Case("static object Run(TestContext context) => Queryable.Select(EntityFrameworkQueryableExtensions.Include(context.Parents, parent => parent.Owner), parent => parent.Id);");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).AsSplitQuery().Select(parent => parent.Id);");
        yield return Case("static object Run(TestContext context) => context.Parents.IgnoreQueryFilters().Include(parent => parent.Owner).IgnoreAutoIncludes().AsTracking().AsSingleQuery().Distinct().TagWithCallSite().Select(parent => parent.Id);");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id).ToList();");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Active ? 1 : 0);");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => new { Id = (long?)parent.Id, parent.Status, Missing = (string?)null });");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(\"Tags\").Include(parent => parent.Owner).Select(parent => parent.Id);");
        yield return Case("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => new ParentDto { Id = parent.Id, OwnerName = parent.Owner.Name }).FirstOrDefaultAsync();");
        yield return new object[]
        {
            QuerySource("""
                static object Run(TestContext context)
                {
                    var first = context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id);
                    var second = context.Parents.Include(parent => parent.Children).Select(parent => parent.Name);
                    return new object[] { first, second };
                }
                """),
            2,
        };
        yield return Case("static object Run(TestContext context) { IQueryable<Parent> query = context.Parents.Include(parent => parent.Owner); query = query.Where(parent => parent.Id > 0); return query.Select(parent => parent.Id); }");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Select(parent => parent.Id);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => new { Entity = parent });");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => new Wrapper(parent));");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Owner);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Select(parent => parent.Children);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Select(parent => new { parent.Id, parent.Children });");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => (parent.Id, parent));");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id > 0 ? parent.Owner : null);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children).Select(parent => parent.Children.Select(child => child.Id).ToList());");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => new KeyValuePair<int, Owner>(parent.Id, parent.Owner));");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => (object)parent.Id);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => new ParentDto { Id = parent.Id, Owner = { Name = parent.Owner.Name } });");
        yield return SourceCase("static object Run(TestContext context, bool active) { IQueryable<Parent> query = context.Parents.Include(parent => parent.Owner); if (active) { query = query.Where(parent => parent.Id > 0); } return query.Select(parent => parent.Id); }");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).ToList().Select(parent => parent.Id);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).AsEnumerable().Select(parent => parent.Id);");
        yield return SourceCase("static object Run(IQueryable<Parent> query) => query.Include(parent => parent.Owner).Select(parent => parent.Id);");
        yield return SourceCase("static object Run() => new[] { new Parent() }.AsQueryable().Include(parent => parent.Owner).Select(parent => parent.Id);");
        yield return SourceCase("static object Run(Store store) => store.Include(value => value.Owner).Select(value => value.Id);", "sealed class Store { public Store Include<T>(Func<Parent, T> path) => this; public int Select(Func<Parent, int> selector) => 0; }");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(\"Owner\").Select(parent => parent.Id);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).WhereActive().Select(parent => parent.Id);", "static class CustomQueries { public static IQueryable<Parent> WhereActive(this IQueryable<Parent> query) => query.Where(parent => parent.Active); }");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent).Select(parent => parent.Id);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select((parent, index) => index);");
        yield return SourceCase("static object Run(TestContext context) => missing.Include(parent => parent.Owner).Select(parent => parent.Id);");
        yield return SourceCase("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Missing);");
        yield return SourceCase("static string Run() => \"Include(Owner).Select(Id)\";");
        yield return SourceCase("""
            static object Run(TestContext context)
            {
            #if NEVER
                return context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id);
            #else
                return context.Parents;
            #endif
            }
            """);
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsIncludesDroppedByNonEntityProjection(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal(ProjectionDropsIncludeAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
            Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
            Assert.Equal("EFD017", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
            Assert.Contains("DbSet origin", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
            Assert.Contains("Queryable.Select", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
            Assert.Contains("does not populate navigation state", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact], StringComparison.Ordinal);
            Assert.Contains("remove the redundant Include", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("return the entity", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
            Assert.Contains("Select(", Text(diagnostic), StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportPreservedAmbiguousOrUnrelatedShapes(string source)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsOnceAtCompleteSelectInvocationWithAllIncludePaths()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Include(parent => parent.Children).ThenInclude(child => child.Toys).Where(parent => parent.Active).Select(parent => new { parent.Id }).ToList();");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("context.Parents.Include(parent => parent.Owner).Include(parent => parent.Children).ThenInclude(child => child.Toys).Where(parent => parent.Active).Select(parent => new { parent.Id })", Text(diagnostic));
        var evidence = diagnostic.Properties[DiagnosticPropertyNames.Evidence];
        Assert.Contains("'context.Parents'", evidence, StringComparison.Ordinal);
        Assert.Contains("Parent.Owner, Parent.Children.Toys", evidence, StringComparison.Ordinal);
        Assert.Contains("an anonymous object with scalar members Id", evidence, StringComparison.Ordinal);
        Assert.Equal(ProjectionDropsIncludeAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
    }

    [Fact]
    public async Task StaticAndReducedSyntaxProduceEquivalentFindings()
    {
        var reduced = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id);")));
        var @static = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context) => Queryable.Select(EntityFrameworkQueryableExtensions.Include(context.Parents, parent => parent.Owner), parent => parent.Id);")));

        Assert.Equal(reduced.Properties[DiagnosticPropertyNames.Evidence], @static.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Equal("Queryable.Select(EntityFrameworkQueryableExtensions.Include(context.Parents, parent => parent.Owner), parent => parent.Id)", Text(@static));
    }

    [Theory]
    [InlineData("parent => parent.Id", "a scalar value of type 'int'")]
    [InlineData("parent => parent.Name", "a scalar value of type 'string'")]
    [InlineData("parent => (parent.Id, parent.Name)", "a tuple '(int Id, string Name)' of scalar values")]
    [InlineData("parent => new ParentSummary(parent.Id, parent.Name)", "a new 'ParentSummary' object built from scalar values")]
    [InlineData("parent => new ParentDto { Id = parent.Id }", "a new 'ParentDto' object built from scalar values")]
    [InlineData("parent => new { parent.Id, OwnerName = parent.Owner.Name }", "an anonymous object with scalar members Id, OwnerName")]
    public async Task EvidenceDescribesProjectionShape(string selector, string expectedShape)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource($"static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select({selector});")));

        Assert.Contains(expectedShape, diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemediationExplainsProjectionAlreadyReadsIncludedNavigation()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Owner.Name);")));

        Assert.Contains("included navigation 'Parent.Owner'", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("already reads data through the included navigation", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("keep the explicit projection", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    public async Task FallsBackToIncludeTextWhenPathIsNotPlainPropertyChain()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => ((Parent)parent).Owner).Select(parent => parent.Id);")));

        Assert.Contains("Include(parent => ((Parent)parent).Owner)", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoversOnlySingleReturnBlockSelectors()
    {
        // Expression trees reject statement lambdas (CS0834), but Roslyn still binds the selector
        // to Queryable.Select, so the recovered single return is classified like an expression body.
        var singleReturn = QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => { return parent.Id; });");
        var multipleStatements = QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => { var id = parent.Id; return id; });");

        Assert.Single(await AnalyzeAsync(singleReturn));
        Assert.Empty(await AnalyzeAsync(multipleStatements));
    }

    [Fact]
    public async Task DoesNotReportEnumerableSelectInsideIncludeOrSelector()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => parent.Children.Select(child => child.Toys)).Where(parent => parent.Children.Select(child => child.Id).Any());");

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task HonorsPragmaSuppressionWhileSiblingStillReports()
    {
        var source = QuerySource("""
            #pragma warning disable EFD017 // Reviewed: include kept to document the aggregate boundary.
            static object Suppressed(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id);
            #pragma warning restore EFD017
            static object Reported(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id);
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HonorsEditorConfigSuppression()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id);");

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new ProjectionDropsIncludeAnalyzer(),
            diagnosticId: ProjectionDropsIncludeAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task HonorsSuppressMessageAttributeWhileSiblingStillReports()
    {
        var source = QuerySource("""
            [SuppressMessage("Correctness", "EFD017", Justification = "Include kept to document the aggregate boundary.")]
            static object Suppressed(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id);
            static object Reported(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id);
            """);

        Assert.Single(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        var source = "// <auto-generated/>\n" + QuerySource("static object Run(TestContext context) => context.Parents.Include(parent => parent.Owner).Select(parent => parent.Id);");

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task DoesNotReportWithoutEntityFrameworkReference()
    {
        var source = """
            using System.Linq;
            static class Subject
            {
                static object Run(IQueryable<int> query) => query.Select(value => value + 1);
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeEntityFramework: false,
            analyzer: new ProjectionDropsIncludeAnalyzer(),
            diagnosticId: ProjectionDropsIncludeAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void DescriptorUsesCorrectnessWarningMetadata()
    {
        Assert.Equal("EFD017", ProjectionDropsIncludeAnalyzer.Rule.Id);
        Assert.Equal("Correctness", ProjectionDropsIncludeAnalyzer.Rule.Category);
        Assert.Equal(DiagnosticSeverity.Warning, ProjectionDropsIncludeAnalyzer.Rule.DefaultSeverity);
        Assert.True(ProjectionDropsIncludeAnalyzer.Rule.IsEnabledByDefault);
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new ProjectionDropsIncludeAnalyzer(),
            diagnosticId: ProjectionDropsIncludeAnalyzer.DiagnosticId);

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

        enum Status { Active, Archived }

        sealed class Toy { public int Id { get; set; } }
        sealed class Tag { public int Id { get; set; } }

        sealed class Owner
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        sealed class Child
        {
            public int Id { get; set; }
            public bool Active { get; set; }
            public List<Toy> Toys { get; set; } = new();
        }

        sealed class Parent
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public bool Active { get; set; }
            public Status Status { get; set; }
            public Owner Owner { get; set; } = new();
            public List<Child> Children { get; set; } = new();
            public List<Tag> Tags { get; set; } = new();
        }

        sealed class OwnerDto { public string Name { get; set; } = ""; }

        sealed class ParentDto
        {
            public int Id { get; set; }
            public string OwnerName { get; set; } = "";
            public OwnerDto Owner { get; } = new();
        }

        sealed record ParentSummary(int Id, string Name);

        sealed class Wrapper
        {
            public Wrapper(Parent parent) => Parent = parent;
            public Parent Parent { get; }
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
