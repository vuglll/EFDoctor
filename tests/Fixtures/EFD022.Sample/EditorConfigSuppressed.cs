using System;
using System.Linq;

namespace EFD022.Sample;

public static class EditorConfigSuppressed
{
    public static IQueryable<Customer> Sample(SampleContext context, string value) =>
        context.Customers.Where(customer => customer.Name.Equals(value, StringComparison.Ordinal));
}
