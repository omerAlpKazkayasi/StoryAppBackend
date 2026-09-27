namespace StoryApp.Application.Reading;

public sealed record ChoiceRequest(
    Guid CurrentNodeId,
    Guid TransitionId);
