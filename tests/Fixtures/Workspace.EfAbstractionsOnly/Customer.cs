using Microsoft.EntityFrameworkCore;

namespace Workspace.EfAbstractionsOnly;

[Index(nameof(Email), IsUnique = true)]
public sealed class Customer
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;
}
