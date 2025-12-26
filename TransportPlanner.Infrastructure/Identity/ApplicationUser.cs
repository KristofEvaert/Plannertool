using Microsoft.AspNetCore.Identity;

namespace TransportPlanner.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public int? OwnerId { get; set; }
}
