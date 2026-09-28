using Microsoft.EntityFrameworkCore;

namespace EFD006.Sample;

public static class EditorConfigSuppressed
{
    public static IQueryable<Blog> Run(SampleContext context) =>
        context.Blogs.Include(blog => blog.Posts).Include(blog => blog.Contributors);
}
