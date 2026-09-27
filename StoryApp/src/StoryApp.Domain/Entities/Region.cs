using StoryApp.Domain.Common;

namespace StoryApp.Domain.Entities;

public class Region : BaseEntity
{
    public Guid StoryUniverseId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? Atmosphere { get; set; }

    public string? VisualStyle { get; set; }

    public string? NaturalElements { get; set; }

    public int MinimumAge { get; set; }

    public int MaximumAge { get; set; }

    public bool IsActive { get; set; } = true;

    public StoryUniverse StoryUniverse { get; set; } = null!;

    public ICollection<Character> Characters { get; set; } = new List<Character>();

    public ICollection<EducationFact> EducationFacts { get; set; } = new List<EducationFact>();
}
