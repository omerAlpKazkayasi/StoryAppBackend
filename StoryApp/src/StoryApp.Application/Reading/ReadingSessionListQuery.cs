namespace StoryApp.Application.Reading;

public sealed record ReadingSessionListQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Status { get; init; }
}
