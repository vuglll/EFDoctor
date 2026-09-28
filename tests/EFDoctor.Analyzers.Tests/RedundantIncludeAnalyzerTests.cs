using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class RedundantIncludeAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts);");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).ThenInclude(post => post.Author).Include(blog => blog.Posts).ThenInclude(post => post.Author);");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Where(blog => blog.Id > 0).OrderBy(blog => blog.Name).AsNoTracking().AsSplitQuery().TagWith(\"report\").Skip(1).Take(5).Include(blog => blog.Posts);");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts).Include(blog => blog.Posts);", 2);
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts).ThenInclude(post => post.Author);");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).ThenInclude(post => post.Author).Include(blog => blog.Posts);");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Owner).Include(blog => blog.Owner.Address);");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(\"Posts.Author\").Include(\"Posts.Author\");");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(\"Posts\").Include(\"Posts.Author\");");
        yield return Case("static object Run(TestContext context) => EntityFrameworkQueryableExtensions.Include(EntityFrameworkQueryableExtensions.Include(context.Blogs, blog => blog.Posts), blog => blog.Posts);");
        yield return Case("static object Run(TestContext context) => context.Set<Blog>().Include(blog => blog.Owner).Include(blog => blog.Owner);");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts).Where(blog => blog.Id > 0).ToList();");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts).Select(blog => blog.Id);");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts).FirstOrDefaultAsync();");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts).Include(blog => blog.Posts).ThenInclude(post => post.Author);", 2);
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).ThenInclude(post => post.Author).ThenInclude(person => person.Address).Include(blog => blog.Posts).ThenInclude(post => post.Author);");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).ThenInclude(post => post.Author.Address).Include(blog => blog.Posts).ThenInclude(post => post.Author);");
        yield return Case("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts.Where(post => post.IsPublished)).Include(blog => blog.Owner).Include(blog => blog.Owner);");
        yield return Case("static object Run(TestContext context) => context.Blogs.IgnoreQueryFilters().Include(blog => blog.Tags).IgnoreAutoIncludes().AsTracking().AsSingleQuery().Distinct().TagWithCallSite().Include(blog => blog.Tags);");
        yield return new object[]
        {
            QuerySource("""
                static object Run(TestContext context)
                {
                    var first = context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts);
                    var second = context.Blogs.Include(blog => blog.Owner).Include(blog => blog.Owner.Address);
                    return new object[] { first, second };
                }
                """),
            2,
        };
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts);");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).ThenInclude(post => post.Author).Include(blog => blog.Posts).ThenInclude(post => post.Tags);");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).ThenInclude(post => post.Author).ThenInclude(person => person.Address).Include(blog => blog.Posts).ThenInclude(post => post.Comments);");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Owner);");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Owner).ThenInclude(person => person.Address).Include(blog => blog.Sponsor).ThenInclude(company => company.Address);");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts.Where(post => post.IsPublished)).Include(blog => blog.Posts);");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts.Where(post => post.IsPublished)).Include(blog => blog.Posts.Where(post => post.IsPublished));");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts).ThenInclude(post => post.Tags.Where(tag => tag.Id > 0));");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => ((Blog)blog).Posts).Include(blog => blog.Posts);");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(\"Posts\").Include(blog => blog.Posts);");
        yield return SourceCase("static object Run(TestContext context, string path) => context.Blogs.Include(path).Include(path);");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(\"Posts.\").Include(\"Posts.\");");
        yield return SourceCase("static object Run(TestContext context, bool active) { IQueryable<Blog> query = context.Blogs.Include(blog => blog.Posts); if (active) { query = query.Where(blog => blog.Active); } return query.Include(blog => blog.Posts); }");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Select(blog => blog).Include(blog => blog.Posts);");
        yield return SourceCase("static object Run(IQueryable<Blog> query) => query.Include(blog => blog.Posts).Include(blog => blog.Posts);");
        yield return SourceCase("static object Run() => new[] { new Blog() }.AsQueryable().Include(blog => blog.Posts).Include(blog => blog.Posts);");
        yield return SourceCase("static object Run(Store store) => store.Include(value => value.Posts).Include(value => value.Posts);", "sealed class Store { public Store Include<T>(Func<Blog, T> path) => this; }");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).WhereActive().Include(blog => blog.Posts);", "static class CustomQueries { public static IQueryable<Blog> WhereActive(this IQueryable<Blog> query) => query.Where(blog => blog.Active); }");
        yield return SourceCase("static object Run(TestContext context) => missing.Include(blog => blog.Posts).Include(blog => blog.Posts);");
        yield return SourceCase("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Missing).Include(blog => blog.Missing);");
        yield return SourceCase("static string Run() => \"Include(Posts).Include(Posts)\";");
        yield return SourceCase("""
            static object Run(TestContext context)
            {
            #if NEVER
                return context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts);
            #else
                return context.Blogs;
            #endif
            }
            """);
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsDuplicateAndCoveredIncludePaths(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
            Assert.Equal(RedundantIncludeAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
            Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
            Assert.Equal("EFD025", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
            Assert.Contains("DbSet origin", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
            Assert.Equal(RedundantIncludeAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
            Assert.Equal(RedundantIncludeAnalyzer.Remediation, diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]);
            Assert.Contains("Include", Text(diagnostic), StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportDistinctBranchingExcludedOrUnrelatedShapes(string source)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "query-chain-local-tracking/Duplicate include split across a local")]
    public async Task DuplicateSplitAcrossALocalIsReportedOnceAtTheLaterInclude()
    {
        var source = QuerySource("""
            static object Run(TestContext context)
            {
                var query = context.Blogs.Include(blog => blog.Posts);
                return query.Include(blog => blog.Posts).ToList();
            }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("Include(blog => blog.Posts)", Text(diagnostic));
        Assert.True(diagnostic.Location.SourceSpan.Start > source.IndexOf("return", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Spec", "query-chain-local-tracking/Include covered by a longer path after the local")]
    public async Task IncludeCoveredByALongerPathAfterALocalIsReportedOnce()
    {
        var source = QuerySource("""
            static object Run(TestContext context)
            {
                var query = context.Blogs.Include(blog => blog.Posts);
                return query.Include(blog => blog.Posts).ThenInclude(post => post.Comments).ToList();
            }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("Include(blog => blog.Posts)", Text(diagnostic));
        Assert.True(diagnostic.Location.SourceSpan.Start < source.IndexOf("return", StringComparison.Ordinal));
        Assert.Contains("covered by a longer path", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedundancyWithinALocalIsReportedOnceAtTheLocal()
    {
        var source = QuerySource("""
            static object Run(TestContext context)
            {
                var query = context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts);
                var first = query.Where(blog => blog.Active).ToList();
                var second = query.Include(blog => blog.Owner).ToList();
                return new object[] { first, second };
            }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.True(diagnostic.Location.SourceSpan.Start < source.IndexOf("var first", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsDuplicateFromIncludeNameThroughLastThenInclude()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).ThenInclude(post => post.Author).Include(blog => blog.Posts).ThenInclude(post => post.Author).ToList();");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("Include(blog => blog.Posts).ThenInclude(post => post.Author)", Text(diagnostic));
        Assert.Equal(source.LastIndexOf(".Include(blog => blog.Posts)", StringComparison.Ordinal) + 1, diagnostic.Location.SourceSpan.Start);
        Assert.Equal(
            "The query is traced to DbSet origin 'context.Blogs'. Include path 'Blog.Posts.Author' is a duplicate: the earlier include chain in the same inline query chain already selects 'Blog.Posts.Author'.",
            diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
    }

    [Fact]
    public async Task ReportsCoveredPrefixWithCoveringPathInEvidence()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts).ThenInclude(post => post.Author);");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("Include(blog => blog.Posts)", Text(diagnostic));
        Assert.Equal(source.IndexOf(".Include(blog => blog.Posts).Include", StringComparison.Ordinal) + 1, diagnostic.Location.SourceSpan.Start);
        Assert.Equal(
            "The query is traced to DbSet origin 'context.Blogs'. Include path 'Blog.Posts' is covered by a longer path: 'Blog.Posts.Author' in the same inline query chain already loads every navigation along it.",
            diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
    }

    [Fact]
    public async Task ReportsStringPathsWithQuotedEvidence()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context) => context.Blogs.Include(\"Posts\").Include(\"Posts.Author\");")));

        Assert.Equal("Include(\"Posts\")", Text(diagnostic));
        Assert.Contains("Include path \"Posts\" is covered by a longer path: \"Posts.Author\"", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticSyntaxReportsOutermostInvocationWithSameEvidence()
    {
        var reduced = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts);")));
        var @static = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context) => EntityFrameworkQueryableExtensions.Include(EntityFrameworkQueryableExtensions.Include(context.Blogs, blog => blog.Posts), blog => blog.Posts);")));

        Assert.Equal(reduced.Properties[DiagnosticPropertyNames.Evidence], @static.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Equal("EntityFrameworkQueryableExtensions.Include(EntityFrameworkQueryableExtensions.Include(context.Blogs, blog => blog.Posts), blog => blog.Posts)", Text(@static));
    }

    [Fact]
    public async Task NeverReportsEarliestOccurrenceOfEachMaximalPath()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts).ThenInclude(post => post.Author).Include(blog => blog.Posts).Include(blog => blog.Owner).Include(blog => blog.Posts).ThenInclude(post => post.Author);");

        var diagnostics = await AnalyzeAsync(source);

        // Chains: Posts, Posts.Author (kept), Posts, Owner (kept), Posts.Author (duplicate).
        Assert.Equal(3, diagnostics.Length);
        var spans = diagnostics.Select(static diagnostic => diagnostic.Location.SourceSpan.Start).OrderBy(static start => start).ToArray();
        var includeStarts = AllIndexesOf(source, "Include(blog =>").ToArray();
        Assert.Equal(new[] { includeStarts[0], includeStarts[2], includeStarts[4] }, spans);
        Assert.Equal(2, diagnostics.Count(static diagnostic => diagnostic.Properties[DiagnosticPropertyNames.Evidence]!.Contains("covered by a longer path", StringComparison.Ordinal)));
        Assert.Single(diagnostics, static diagnostic => diagnostic.Properties[DiagnosticPropertyNames.Evidence]!.Contains("is a duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsChainThatIsBothDuplicateAndCoveredOnce()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts).ThenInclude(post => post.Author).Include(blog => blog.Posts);");

        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, static diagnostic => Assert.Contains("covered by a longer path", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(".Where(blog => blog.Active).ToList()")]
    [InlineData(".Select(blog => blog.Id)")]
    [InlineData(".ToList()")]
    [InlineData(".Where(blog => blog.Active).OrderBy(blog => blog.Id)")]
    public async Task AnalyzesEachInlineChainOnce(string suffix)
    {
        var source = QuerySource($"static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts){suffix};");

        Assert.Single(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task HonorsPragmaSuppressionWhileSiblingStillReports()
    {
        var source = QuerySource("""
            #pragma warning disable EFD025 // Reviewed: include kept to document the aggregate boundary.
            static object Suppressed(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts);
            #pragma warning restore EFD025
            static object Reported(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts);
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HonorsEditorConfigSuppression()
    {
        var source = QuerySource("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts);");

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new RedundantIncludeAnalyzer(),
            diagnosticId: RedundantIncludeAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task HonorsSuppressMessageAttributeWhileSiblingStillReports()
    {
        var source = QuerySource("""
            [SuppressMessage("Maintainability", "EFD025", Justification = "Include kept to document the aggregate boundary.")]
            static object Suppressed(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts);
            static object Reported(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts);
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        var source = "// <auto-generated/>\n" + QuerySource("static object Run(TestContext context) => context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Posts);");

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task DoesNotReportWithoutEntityFrameworkReference()
    {
        var source = """
            using System.Linq;
            static class Subject
            {
                static object Run(IQueryable<int> query) => query.Where(value => value > 0).Where(value => value > 0);
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeEntityFramework: false,
            analyzer: new RedundantIncludeAnalyzer(),
            diagnosticId: RedundantIncludeAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void DescriptorUsesMaintainabilityInfoMetadata()
    {
        Assert.Equal("EFD025", RedundantIncludeAnalyzer.Rule.Id);
        Assert.Equal("Maintainability", RedundantIncludeAnalyzer.Rule.Category);
        Assert.Equal(DiagnosticSeverity.Info, RedundantIncludeAnalyzer.Rule.DefaultSeverity);
        Assert.True(RedundantIncludeAnalyzer.Rule.IsEnabledByDefault);
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new RedundantIncludeAnalyzer(),
            diagnosticId: RedundantIncludeAnalyzer.DiagnosticId);

    private static object[] Case(string member, int expectedCount = 1) => [QuerySource(member), expectedCount];

    private static object[] SourceCase(string member, string additionalTypes = "") => [QuerySource(member, additionalTypes)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static IEnumerable<int> AllIndexesOf(string text, string value)
    {
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            yield return index;
        }
    }

    private static string QuerySource(string member, string additionalTypes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Address { public int Id { get; set; } }
        sealed class Tag { public int Id { get; set; } }
        sealed class Comment { public int Id { get; set; } }

        sealed class Person
        {
            public int Id { get; set; }
            public Address Address { get; set; } = new();
        }

        sealed class Company
        {
            public int Id { get; set; }
            public Address Address { get; set; } = new();
        }

        sealed class Post
        {
            public int Id { get; set; }
            public bool IsPublished { get; set; }
            public Person Author { get; set; } = new();
            public List<Tag> Tags { get; set; } = new();
            public List<Comment> Comments { get; set; } = new();
        }

        sealed class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public bool Active { get; set; }
            public Person Owner { get; set; } = new();
            public Company Sponsor { get; set; } = new();
            public List<Post> Posts { get; set; } = new();
            public List<Tag> Tags { get; set; } = new();
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Blog> Blogs => Set<Blog>();
        }

        {{additionalTypes}}

        static class Subject
        {
            {{member}}
        }
        """;
}
