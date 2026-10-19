using Microsoft.EntityFrameworkCore;

namespace EFD024.Sample;

public static class EditorConfigSuppressed
{
    public static List<Post> Sample(SampleContext context, int id)
    {
        var blog = context.Blogs.Include(candidate => candidate.Posts).First(candidate => candidate.Id == id);
        return blog.Posts.Where(post => post.Published).ToList();
    }
}
