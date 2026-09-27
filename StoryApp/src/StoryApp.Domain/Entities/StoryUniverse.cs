using StoryApp.Domain.Common;

namespace StoryApp.Domain.Entities;

public class StoryUniverse : BaseEntity
{
    public Guid ThemeId { get; set; }

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? Tone { get; set; }

    public int MinimumAge { get; set; }

    public int MaximumAge { get; set; }

    public bool IsActive { get; set; } = true;

    public Theme Theme { get; set; } = null!;

    public ICollection<Region> Regions { get; set; } = new List<Region>();

    public ICollection<Character> Characters { get; set; } = new List<Character>();

    public ICollection<StoryObject> Objects { get; set; } = new List<StoryObject>();

    public ICollection<EducationFact> EducationFacts { get; set; } = new List<EducationFact>();

    public ICollection<Story> Stories { get; set; } = new List<Story>();
}
