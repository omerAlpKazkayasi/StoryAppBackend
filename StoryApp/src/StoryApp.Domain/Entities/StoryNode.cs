using StoryApp.Domain.Common;

namespace StoryApp.Domain.Entities;

public class StoryNode : BaseEntity
{
    public Guid StoryId { get; set; }

    public int PartNumber { get; set; }

    public string Title { get; set; } = null!;

    public bool IsEnding { get; set; }

    public Story Story { get; set; } = null!;

    public StoryNodeMemory? Memory { get; set; }

    public ICollection<StoryNodeScene> Scenes { get; set; } = new List<StoryNodeScene>();

    public ICollection<StoryTransition> OutgoingTransitions { get; set; } = new List<StoryTransition>();

    public ICollection<StoryTransition> IncomingTransitions { get; set; } = new List<StoryTransition>();
}
