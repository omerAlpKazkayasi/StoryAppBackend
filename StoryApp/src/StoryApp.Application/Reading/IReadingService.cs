namespace StoryApp.Application.Reading;

public interface IReadingService
{
    Task<ReadingSessionResponse> ChooseAsync(
        Guid userId,
        Guid sessionId,
        Guid currentNodeId,
        Guid transitionId,
        CancellationToken cancellationToken = default);

    Task<ReadingSessionResponse> BackAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<ReadingSessionResponse> GetAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<ReadingSessionListResponse> GetListAsync(
        Guid userId,
        ReadingSessionListQuery query,
        CancellationToken cancellationToken = default);

    Task AbandonAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<ReadingSessionResponse> RestartAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
