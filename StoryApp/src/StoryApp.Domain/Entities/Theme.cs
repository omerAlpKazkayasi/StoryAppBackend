using StoryApp.Domain.Common;

namespace StoryApp.Domain.Entities;

public class Theme : BaseEntity
{
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? Icon { get; set; }

    public string? Color { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<StoryUniverse> Universes { get; set; } = new List<StoryUniverse>();
}
