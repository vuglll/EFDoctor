using Microsoft.EntityFrameworkCore;

namespace EFD009.Sample;

public static class EditorConfigSuppressed
{
    public static IQueryable<Customer> Run(SampleContext context, string email) =>
        context.Customers.Where(customer => customer.Email.ToLower() == email);
}
