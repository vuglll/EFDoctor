using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace EFD024.Sample;

public sealed class Post
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool Published { get; set; }
}

public sealed class Blog
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Post> Posts { get; set; } = new();
}

public sealed class SampleContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();
}

public static class IncludeCases
{
    // Reported: every post is loaded, and only the published ones are used.
    public static async Task<List<string>> Reported(SampleContext context, int id)
    {
        var blog = await context.Blogs.Include(candidate => candidate.Posts).FirstAsync(candidate => candidate.Id == id);
        return blog.Posts.Where(post => post.Published).Select(post => post.Title).ToList();
    }

    // Not reported: the filtered Include filters in SQL.
    public static List<Post> FilteredInclude(SampleContext context, int id)
    {
        var blog = context.Blogs.Include(candidate => candidate.Posts.Where(post => post.Published)).First(candidate => candidate.Id == id);
        return blog.Posts.ToList();
    }

    // Not reported: all posts are used too.
    public static (int All, int Published) BothUsed(SampleContext context, int id)
    {
        var blog = context.Blogs.Include(candidate => candidate.Posts).First(candidate => candidate.Id == id);
        return (blog.Posts.Count, blog.Posts.Count(post => post.Published));
    }

    // Not reported: suppressed with a recorded reason.
    [SuppressMessage("Performance", "EFD024", Justification = "A blog has a handful of posts.")]
    public static List<Post> Suppressed(SampleContext context, int id)
    {
        var blog = context.Blogs.Include(candidate => candidate.Posts).First(candidate => candidate.Id == id);
        return blog.Posts.Where(post => post.Published).ToList();
    }
}
