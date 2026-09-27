using StoryApp.Domain.Common;

namespace StoryApp.Domain.Entities;

public class StoryNodeScene : BaseEntity
{
    public Guid StoryNodeId { get; set; }

    public int SortOrder { get; set; }

    public string Text { get; set; } = null!;

    public string? ImageObjectKey { get; set; }

    public StoryNode StoryNode { get; set; } = null!;
}
