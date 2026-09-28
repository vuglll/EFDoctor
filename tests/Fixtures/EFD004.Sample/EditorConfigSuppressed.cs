using Microsoft.EntityFrameworkCore;

namespace EFD004.Sample;

public static class EditorConfigSuppressed
{
    public static IEnumerable<Customer> Run(SampleContext context) =>
        context.Customers.ToList().Where(customer => customer.Active);
}
