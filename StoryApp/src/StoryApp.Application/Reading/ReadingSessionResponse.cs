namespace StoryApp.Application.Reading;

public sealed record ReadingSessionResponse(
    Guid SessionId,
    Guid StoryId,
    string? StoryTitle,
    string Status,
    bool CanGoBack,
    ReadingNodeResponse CurrentNode);
