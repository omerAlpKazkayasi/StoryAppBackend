using StoryApp.Domain.Common;

namespace StoryApp.Domain.Entities;

public class StoryObject : BaseEntity
{
    public Guid StoryUniverseId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? VisualDescription { get; set; }

    public string? StoryFunction { get; set; }

    public bool IsMagical { get; set; }

    public bool IsActive { get; set; } = true;

    public StoryUniverse StoryUniverse { get; set; } = null!;
}
