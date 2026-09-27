namespace StoryApp.Application.Reading;

public sealed record ReadingNodeResponse(
    Guid Id,
    string Title,
    bool IsEnding,
    IReadOnlyList<ReadingSceneResponse> Scenes,
    IReadOnlyList<ReadingChoiceResponse> Choices)
{
    public IReadOnlyList<ReadingSceneResponse> Scenes { get; init; } = Scenes ?? [];
    public IReadOnlyList<ReadingChoiceResponse> Choices { get; init; } = Choices ?? [];
}
