namespace StoryApp.Application.Reading;

public sealed record ReadingChoiceResponse(
    Guid TransitionId,
    string Title,
    int SortOrder);
