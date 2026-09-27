using Microsoft.AspNetCore.Identity;
using StoryApp.Domain.Enums;

namespace StoryApp.Infrastructure.Identity;

public class AppUser : IdentityUser<Guid>
{
    public UserAccountType AccountType { get; set; } = UserAccountType.Guest;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
