namespace StoryApp.Domain.Entities;

public class StoryReadingHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReadingSessionId { get; set; }

    public int StepNumber { get; set; }

    public Guid FromNodeId { get; set; }

    public Guid TransitionId { get; set; }

    public Guid ToNodeId { get; set; }

    public DateTime SelectedAt { get; set; } = DateTime.UtcNow;

    public StoryReadingSession ReadingSession { get; set; } = null!;
}
