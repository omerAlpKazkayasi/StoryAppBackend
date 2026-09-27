namespace StoryApp.Application.Reading;

public sealed record ReadingSceneResponse(
    Guid Id,
    int SortOrder,
    string Text,
    string? ImageObjectKey);
