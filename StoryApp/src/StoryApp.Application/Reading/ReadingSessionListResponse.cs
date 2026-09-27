namespace StoryApp.Application.Reading;

public sealed record ReadingSessionListResponse(
    IReadOnlyList<ReadingSessionListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages)
{
    public IReadOnlyList<ReadingSessionListItemResponse> Items { get; init; } = Items ?? [];
}
