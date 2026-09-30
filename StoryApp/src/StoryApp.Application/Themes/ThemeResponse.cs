namespace StoryApp.Application.Themes;

public sealed record ThemeResponse(
    Guid Id,
    string Name,
    string? Description,
    string? Icon,
    string? Color,
    string? ImagePath,
    int SortOrder);

