namespace StoryApp.Application.Reading;

public interface IPlayService
{
    Task<ReadingSessionResponse> StartAsync(
        Guid userId,
        Guid themeId,
        CancellationToken cancellationToken = default);
}
