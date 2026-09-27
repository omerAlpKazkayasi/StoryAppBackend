namespace StoryApp.Application.Reading;

public sealed record ReadingSessionListItemResponse(
    Guid SessionId,
    Guid StoryId,
    string? StoryTitle,
    string Status,
    DateTime StartedAt,
    DateTime LastReadAt,
    DateTime? CompletedAt);
