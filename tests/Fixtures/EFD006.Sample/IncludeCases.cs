using Microsoft.EntityFrameworkCore;

namespace EFD006.Sample;

public sealed class Blog
{
    public int Id { get; set; }
    public List<Post> Posts { get; set; } = new();
    public List<Contributor> Contributors { get; set; } = new();
    public List<Tag> Tags { get; set; } = new();
}

public sealed class Post { public int Id { get; set; } }
public sealed class Contributor { public int Id { get; set; } }
public sealed class Tag { public int Id { get; set; } }

public sealed class SampleContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();
}

public static class IncludeCases
{
    public static IQueryable<Blog> ReportedTwoCollections(SampleContext context) =>
        context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Contributors);

    public static IQueryable<Blog> ReportedThreeCollections(SampleContext context) =>
        context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Contributors).Include(blog => blog.Tags);

    public static IQueryable<Blog> SplitQuery(SampleContext context) =>
        context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Contributors).AsSplitQuery();

#pragma warning disable EFD006 // Reviewed: split-query behavior is configured globally for this context.
    public static IQueryable<Blog> PragmaSuppressed(SampleContext context) =>
        context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Contributors);
#pragma warning restore EFD006

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "EFD006",
        Justification = "Split-query behavior is configured globally for this context.")]
    public static IQueryable<Blog> AttributeSuppressed(SampleContext context) =>
        context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Contributors);
}
