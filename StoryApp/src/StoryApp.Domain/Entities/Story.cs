using StoryApp.Domain.Common;
using StoryApp.Domain.Enums;

namespace StoryApp.Domain.Entities;

public class Story : BaseEntity
{
    public Guid UniverseId { get; set; }

    public int ChildAge { get; set; }

    public string Language { get; set; } = null!;

    public string? Title { get; set; }

    public StoryStatus Status { get; set; }

    public Guid? RootNodeId { get; set; }

    public string? FailureReason { get; set; }

    public DateTime? CompletedAt { get; set; }

    public StoryUniverse Universe { get; set; } = null!;

    public StoryNode? RootNode { get; set; }

    public ICollection<StoryNode> Nodes { get; set; } = new List<StoryNode>();
}
