using StoryApp.Domain.Enums;

namespace StoryApp.Domain.Entities;

public class StoryReadingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid StoryId { get; set; }

    public Guid CurrentNodeId { get; set; }

    public int CurrentStep { get; set; } = 0;

    public StoryReadingStatus Status { get; set; } = StoryReadingStatus.InProgress;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastReadAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public Story Story { get; set; } = null!;

    public StoryNode CurrentNode { get; set; } = null!;

    public ICollection<StoryReadingHistory> Histories { get; set; } = new List<StoryReadingHistory>();
}
