using Microsoft.EntityFrameworkCore;

namespace EFD023.Sample;

public static class EditorConfigSuppressed
{
    public static IQueryable<Customer> Run(SampleContext context, string term) =>
        context.Customers.Where(customer => customer.Name.Contains(term));
}
