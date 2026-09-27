using StoryApp.Domain.Common;

namespace StoryApp.Domain.Entities;

public class EducationFact : BaseEntity
{
    public Guid StoryUniverseId { get; set; }

    public Guid? RegionId { get; set; }

    public string Topic { get; set; } = null!;

    public string Fact { get; set; } = null!;

    public string? UsageHint { get; set; }

    public int MinimumAge { get; set; }

    public int MaximumAge { get; set; }

    public bool IsVerified { get; set; }

    public bool IsActive { get; set; } = true;

    public StoryUniverse StoryUniverse { get; set; } = null!;

    public Region? Region { get; set; }
}
