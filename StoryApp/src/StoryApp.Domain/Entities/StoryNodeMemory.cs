using StoryApp.Domain.Common;

namespace StoryApp.Domain.Entities;

public class StoryNodeMemory : BaseEntity
{
    public Guid StoryNodeId { get; set; }

    public string Summary { get; set; } = null!;

    public Guid MainCharacterId { get; set; }

    public Guid? CurrentRegionId { get; set; }

    public string? CurrentGoal { get; set; }

    public string CompanionCharacterIdsJson { get; set; } = "[]";

    public string ActiveObjectIdsJson { get; set; } = "[]";

    public string OpenThreadsJson { get; set; } = "[]";

    public string ImportantEventsJson { get; set; } = "[]";

    public string EstablishedFactsJson { get; set; } = "[]";

    public string? NarrativeAngleUsed { get; set; }

    public StoryNode StoryNode { get; set; } = null!;
}
