using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class IncludeFilteredInMemoryAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); return blog.Posts.Where(p => p.Published).ToList(); }", "db.Blogs.Include(b => b.Posts)");
        yield return Case("static async Task<int> Run(BlogContext db, int id) { var blog = await db.Blogs.Include(b => b.Posts).FirstAsync(b => b.Id == id); return blog.Posts.Count(p => p.Published); }", "db.Blogs.Include(b => b.Posts)");
        yield return Case("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).SingleOrDefault(b => b.Id == id); if (blog == null) return 0; return blog.Posts.Where(p => p.Published).Select(p => p.Title).ToList(); }", "db.Blogs.Include(b => b.Posts)");
        yield return Case("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); var any = blog.Posts.Any(p => p.Published); var first = blog.Posts.FirstOrDefault(p => p.Pinned); return (blog.Name, any, first); }", "db.Blogs.Include(b => b.Posts)");
        yield return Case("static void Run(BlogContext db) { var blogs = db.Blogs.Include(b => b.Posts).ToList(); foreach (var blog in blogs) { Console.WriteLine(blog.Posts.Count(p => p.Published)); } }", "db.Blogs.Include(b => b.Posts)");
        yield return Case("static object Run(BlogContext db) { var blogs = db.Blogs.Include(b => b.Posts).ToList(); return blogs.Select(b => new { b.Name, Published = b.Posts.Where(p => p.Published).ToList() }).ToList(); }", "db.Blogs.Include(b => b.Posts)");
        yield return Case("static async Task<object> Run(BlogContext db) { var blogs = await db.Blogs.Include(b => b.Posts).ToListAsync(); return blogs.Where(b => b.Posts.Any(p => p.Pinned)).Select(b => b.Name).ToList(); }", "db.Blogs.Include(b => b.Posts)");
        yield return Case("static object Run(BlogContext db, int id) { var blog = db.Blogs.AsNoTracking().Include(b => b.Posts).Where(b => b.Id == id).First(); return blog.Posts.Where(p => p.Published).ToList(); }", "db.Blogs.AsNoTracking().Include(b => b.Posts)");
        yield return Case("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).Include(b => b.Owner).First(b => b.Id == id); return (blog.Owner.Name, blog.Posts.Where(p => p.Published).ToList()); }", "db.Blogs.Include(b => b.Posts)");
        yield return Case("static object Run(BlogContext db) { var blogs = db.Blogs.Include(b => b.Posts).ToArray(); var count = blogs.Length; return blogs.Select(b => b.Posts.LongCount(p => p.Published)).ToList(); }", "db.Blogs.Include(b => b.Posts)");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); return blog.Posts.Count; }");
        yield return Negative("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); var published = blog.Posts.Where(p => p.Published).ToList(); return (published, blog.Posts.Count); }");
        yield return Negative("static void Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); foreach (var post in blog.Posts) { Console.WriteLine(post.Title); } }");
        yield return Negative("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); return blog.Posts.OrderBy(p => p.Title).ToList(); }");
        yield return Negative("static Blog Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); var published = blog.Posts.Where(p => p.Published).ToList(); return blog; }");
        yield return Negative("static void Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); Console.WriteLine(blog.Posts.Where(p => p.Published).Count()); Save(blog); }\nstatic void Save(Blog blog) { }");
        yield return Negative("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts.Where(p => p.Published)).First(b => b.Id == id); return blog.Posts.Where(p => p.Pinned).ToList(); }");
        yield return Negative("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Owner).First(b => b.Id == id); return blog.Owner.Name; }");
        yield return Negative("static object Run(BlogContext db) { var blogs = db.Blogs.Include(b => b.Posts).ToList(); return blogs; }");
        yield return Negative("static object Run(BlogContext db) { var blogs = db.Blogs.Include(b => b.Posts).ToList(); return blogs.Select(b => b.Posts.Count).ToList(); }");
        yield return Negative("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); return blog.Name; }");
        yield return Negative("static object Run(List<Blog> source) { var blog = source.AsQueryable().Include(b => b.Posts).First(); return blog.Posts.Where(p => p.Published).ToList(); }");
    }

    [Theory]
    [Trait("Spec", "efd024-include-filtered-in-memory/Single entity with a filtered navigation")]
    [Trait("Spec", "efd024-include-filtered-in-memory/List iterated with foreach or projected")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsIncludesOnlyUsedFiltered(string source, string expectedText)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal(expectedText, Text(diagnostic));
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "efd024-include-filtered-in-memory/Navigation also used in full")]
    [Trait("Spec", "efd024-include-filtered-in-memory/Entity escapes")]
    [Trait("Spec", "efd024-include-filtered-in-memory/Filtered Include or reference navigation")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportFullOrEscapingUses(string source)
    {
        Assert.DoesNotContain(AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd024-include-filtered-in-memory/Complete structured finding")]
    public async Task FindingCarriesTheCompleteContract()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(Source("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); return blog.Posts.Where(p => p.Published).ToList(); }")));

        Assert.Equal(IncludeFilteredInMemoryAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("Performance", diagnostic.Descriptor.Category);
        Assert.Equal("'Posts' is loaded in full by Include, but this method only uses it filtered; use a filtered Include", diagnostic.GetMessage());
        Assert.Equal("advisory", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD024", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal("'db.Blogs.Include(b => b.Posts)' loads every 'Posts' row for the entities in 'blog', loaded by 'First', and each of the 1 use(s) of 'Posts' in this method filters it in memory with a predicate.", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Equal(IncludeFilteredInMemoryAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("Include(b => b.Posts.Where(p => p.Published))", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd024-include-filtered-in-memory/Suppression")]
    public async Task HonorsPragmaSuppressMessageAndEditorConfig()
    {
        var source = Source("""
            static object Pragma(BlogContext db, int id)
            {
            #pragma warning disable EFD024
                var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id);
            #pragma warning restore EFD024
                return blog.Posts.Where(p => p.Published).ToList();
            }

            [SuppressMessage("Performance", "EFD024", Justification = "Posts per blog are few.")]
            static object Attributed(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); return blog.Posts.Where(p => p.Published).ToList(); }

            static object Reported(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); return blog.Posts.Where(p => p.Pinned).ToList(); }
            """);

        Assert.Single(await AnalyzeAsync(source));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new IncludeFilteredInMemoryAnalyzer(),
            diagnosticId: IncludeFilteredInMemoryAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + Source("static object Run(BlogContext db, int id) { var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id); return blog.Posts.Where(p => p.Published).ToList(); }")));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new IncludeFilteredInMemoryAnalyzer(),
            diagnosticId: IncludeFilteredInMemoryAnalyzer.DiagnosticId);

    private static object[] Case(string member, string expectedText) => [Source(member), expectedText];

    private static object[] Negative(string member) => [Source(member)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string Source(string member) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Post
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public bool Published { get; set; }
            public bool Pinned { get; set; }
        }

        sealed class Person
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        sealed class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<Post> Posts { get; set; } = new();
            public Person Owner { get; set; } = new();
        }

        sealed class BlogContext : DbContext
        {
            public DbSet<Blog> Blogs => Set<Blog>();
        }

        static class Subject
        {
            {{member}}
        }
        """;
}
