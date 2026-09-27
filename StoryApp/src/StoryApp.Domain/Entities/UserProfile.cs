namespace StoryApp.Domain.Entities;

public class UserProfile
{
    public Guid UserId { get; set; }

    public int? ChildAge { get; set; }

    public string? PreferredLanguage { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
