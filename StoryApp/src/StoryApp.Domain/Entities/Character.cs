using StoryApp.Domain.Common;

namespace StoryApp.Domain.Entities;

public class Character : BaseEntity
{
    public Guid StoryUniverseId { get; set; }

    public Guid RegionId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string Personality { get; set; } = null!;

    public string? Motivation { get; set; }

    public string? SpeakingStyle { get; set; }

    public string? VisualDescription { get; set; }

    public string? SpecialAbility { get; set; }

    public int MinimumAge { get; set; }

    public int MaximumAge { get; set; }

    public bool CanBeMainCharacter { get; set; }

    public bool CanBeSupportingCharacter { get; set; }

    public bool IsActive { get; set; } = true;

    public StoryUniverse StoryUniverse { get; set; } = null!;

    public Region Region { get; set; } = null!;
}
