namespace StoryApp.Domain.Entities;

public class StoryTransition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FromNodeId { get; set; }

    public Guid? ToNodeId { get; set; }

    public string ChoiceTitle { get; set; } = null!;

    public string ChoiceIntent { get; set; } = null!;

    public int NextTargetMomentum { get; set; }

    public bool MeetNewCharacter { get; set; }

    public bool ChangeRegion { get; set; }

    public bool IntroduceNewObject { get; set; }

    public bool IncludeEducation { get; set; }

    public int SortOrder { get; set; }

    public StoryNode FromNode { get; set; } = null!;

    public StoryNode? ToNode { get; set; }
}
