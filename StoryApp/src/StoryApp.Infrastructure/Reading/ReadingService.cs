using Microsoft.EntityFrameworkCore;
using StoryApp.Application.Common.Exceptions;
using StoryApp.Application.Reading;
using StoryApp.Domain.Entities;
using StoryApp.Domain.Enums;
using StoryApp.Infrastructure.Persistence;

namespace StoryApp.Infrastructure.Reading;

public sealed class ReadingService : IReadingService
{
    private readonly AppDbContext _dbContext;

    public ReadingService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReadingSessionResponse> ChooseAsync(
        Guid userId,
        Guid sessionId,
        Guid currentNodeId,
        Guid transitionId,
        CancellationToken cancellationToken = default)
    {
        // 1. Session lookup and ownership check
        var session = await _dbContext.StoryReadingSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .Select(s => new
            {
                s.Id,
                s.UserId,
                s.StoryId,
                s.CurrentNodeId,
                s.CurrentStep,
                s.Status,
                StoryTitle = s.Story.Title
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null)
        {
            throw new NotFoundException("Reading session not found.");
        }

        // 2. Validate session status (must be InProgress)
        if (session.Status != StoryReadingStatus.InProgress)
        {
            throw new ConflictException("Reading session is not active.");
        }

        // 3. Stale current node check
        if (session.CurrentNodeId != currentNodeId)
        {
            throw new ConflictException("The reading session has already moved to another node.");
        }

        // 4. Transition & Target node validation
        var transition = await _dbContext.StoryTransitions
            .AsNoTracking()
            .Where(t => t.Id == transitionId
                     && t.FromNodeId == currentNodeId
                     && t.FromNode.StoryId == session.StoryId
                     && t.ToNodeId != null)
            .Select(t => new
            {
                t.Id,
                ToNodeId = t.ToNodeId!.Value,
                TargetNode = _dbContext.StoryNodes
                    .Where(n => n.Id == t.ToNodeId && n.StoryId == session.StoryId)
                    .Select(n => new
                    {
                        n.Id,
                        n.IsEnding
                    })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (transition == null || transition.TargetNode == null)
        {
            throw new ConflictException("Choice is not available for the current node.");
        }

        var targetNode = transition.TargetNode;
        var previousStep = session.CurrentStep;
        var nextStep = previousStep + 1;
        var utcNow = DateTime.UtcNow;
        var isEnding = targetNode.IsEnding;
        var newStatus = isEnding ? StoryReadingStatus.Completed : StoryReadingStatus.InProgress;
        DateTime? completedAt = isEnding ? utcNow : null;

        // 5. Atomic compare-and-swap database transaction
        await using var dbTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rowsUpdated = await _dbContext.StoryReadingSessions
            .Where(s => s.Id == sessionId
                     && s.UserId == userId
                     && s.Status == StoryReadingStatus.InProgress
                     && s.CurrentNodeId == currentNodeId
                     && s.CurrentStep == previousStep)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.CurrentNodeId, targetNode.Id)
                .SetProperty(s => s.CurrentStep, nextStep)
                .SetProperty(s => s.LastReadAt, utcNow)
                .SetProperty(s => s.Status, newStatus)
                .SetProperty(s => s.CompletedAt, completedAt),
                cancellationToken);

        if (rowsUpdated != 1)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw new ConflictException("The reading session has changed. Refresh the current state and try again.");
        }

        // Cleanup future history if re-choosing after rewind
        await _dbContext.StoryReadingHistories
            .Where(h => h.ReadingSessionId == sessionId && h.StepNumber > previousStep)
            .ExecuteDeleteAsync(cancellationToken);

        // Insert new history record
        var history = new StoryReadingHistory
        {
            Id = Guid.NewGuid(),
            ReadingSessionId = sessionId,
            StepNumber = nextStep,
            FromNodeId = currentNodeId,
            TransitionId = transition.Id,
            ToNodeId = targetNode.Id,
            SelectedAt = utcNow
        };

        _dbContext.StoryReadingHistories.Add(history);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await dbTransaction.CommitAsync(cancellationToken);

        // 6. Build target node response
        var targetNodeResponse = await ReadingResponseBuilder.BuildNodeResponseAsync(
            _dbContext,
            targetNode.Id,
            cancellationToken);

        var statusString = isEnding ? "Completed" : "InProgress";
        var canGoBack = nextStep > 0;

        return new ReadingSessionResponse(
            sessionId,
            session.StoryId,
            session.StoryTitle,
            statusString,
            canGoBack,
            targetNodeResponse);
    }

    public async Task<ReadingSessionResponse> BackAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        // 1. Session lookup and ownership check
        var session = await _dbContext.StoryReadingSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .Select(s => new
            {
                s.Id,
                s.UserId,
                s.StoryId,
                s.CurrentNodeId,
                s.CurrentStep,
                s.Status,
                StoryTitle = s.Story.Title
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null)
        {
            throw new NotFoundException("Reading session not found.");
        }

        // 2. Status check: InProgress or Completed can go back; Abandoned cannot
        if (session.Status == StoryReadingStatus.Abandoned)
        {
            throw new ConflictException("Reading session is not active.");
        }

        // 3. CurrentStep check: must be > 0 (cannot go back from root)
        if (session.CurrentStep <= 0)
        {
            throw new ConflictException("Cannot go back from the start of the story.");
        }

        // 4. Lookup history for CurrentStep: StepNumber == session.CurrentStep
        var history = await _dbContext.StoryReadingHistories
            .AsNoTracking()
            .Where(h => h.ReadingSessionId == sessionId && h.StepNumber == session.CurrentStep)
            .Select(h => new
            {
                h.FromNodeId,
                h.ToNodeId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (history == null || history.ToNodeId != session.CurrentNodeId)
        {
            throw new ConflictException("Reading history is not consistent with the current session state.");
        }

        // Verify history.FromNodeId belongs to session's Story
        var fromNodeValid = await _dbContext.StoryNodes
            .AsNoTracking()
            .AnyAsync(n => n.Id == history.FromNodeId && n.StoryId == session.StoryId, cancellationToken);

        if (!fromNodeValid)
        {
            throw new ConflictException("Reading history is not consistent with the current session state.");
        }

        var expectedCurrentStep = session.CurrentStep;
        var expectedCurrentNodeId = session.CurrentNodeId;
        var expectedStatus = session.Status;
        var newCurrentStep = expectedCurrentStep - 1;
        var utcNow = DateTime.UtcNow;

        // 5. Compare-and-swap database update
        var rowsUpdated = await _dbContext.StoryReadingSessions
            .Where(s => s.Id == sessionId
                     && s.UserId == userId
                     && s.CurrentStep == expectedCurrentStep
                     && s.CurrentNodeId == expectedCurrentNodeId
                     && s.Status == expectedStatus)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.CurrentNodeId, history.FromNodeId)
                .SetProperty(s => s.CurrentStep, newCurrentStep)
                .SetProperty(s => s.LastReadAt, utcNow)
                .SetProperty(s => s.Status, StoryReadingStatus.InProgress)
                .SetProperty(s => s.CompletedAt, (DateTime?)null),
                cancellationToken);

        if (rowsUpdated != 1)
        {
            throw new ConflictException("The reading session has changed. Refresh the current state and try again.");
        }

        // 6. Build response for previous node
        var previousNodeResponse = await ReadingResponseBuilder.BuildNodeResponseAsync(
            _dbContext,
            history.FromNodeId,
            cancellationToken);

        var canGoBack = newCurrentStep > 0;

        return new ReadingSessionResponse(
            sessionId,
            session.StoryId,
            session.StoryTitle,
            "InProgress",
            canGoBack,
            previousNodeResponse);
    }

    public async Task<ReadingSessionResponse> GetAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        // 1. Session lookup and ownership check (read-only)
        var session = await _dbContext.StoryReadingSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .Select(s => new
            {
                s.Id,
                s.StoryId,
                StoryTitle = s.Story.Title,
                s.CurrentNodeId,
                s.CurrentStep,
                s.Status
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null)
        {
            throw new NotFoundException("Reading session not found.");
        }

        // 2. Build current node response
        var currentNodeResponse = await ReadingResponseBuilder.BuildNodeResponseAsync(
            _dbContext,
            session.CurrentNodeId,
            cancellationToken);

        var statusString = session.Status switch
        {
            StoryReadingStatus.InProgress => "InProgress",
            StoryReadingStatus.Completed => "Completed",
            StoryReadingStatus.Abandoned => "Abandoned",
            _ => session.Status.ToString()
        };

        var canGoBack = session.CurrentStep > 0;

        return new ReadingSessionResponse(
            session.Id,
            session.StoryId,
            session.StoryTitle,
            statusString,
            canGoBack,
            currentNodeResponse);
    }

    public async Task<ReadingSessionListResponse> GetListAsync(
        Guid userId,
        ReadingSessionListQuery query,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.StoryReadingSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (Enum.TryParse<StoryReadingStatus>(query.Status, ignoreCase: true, out var parsedStatus)
                && Enum.IsDefined(parsedStatus))
            {
                baseQuery = baseQuery.Where(s => s.Status == parsedStatus);
            }
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var items = await baseQuery
            .OrderByDescending(s => s.LastReadAt)
            .ThenByDescending(s => s.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new ReadingSessionListItemResponse(
                s.Id,
                s.StoryId,
                s.Story.Title,
                s.Status == StoryReadingStatus.InProgress
                    ? "InProgress"
                    : s.Status == StoryReadingStatus.Completed
                        ? "Completed"
                        : "Abandoned",
                s.StartedAt,
                s.LastReadAt,
                s.CompletedAt))
            .ToListAsync(cancellationToken);

        return new ReadingSessionListResponse(
            items,
            query.Page,
            query.PageSize,
            totalCount,
            totalPages);
    }

    public async Task AbandonAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.StoryReadingSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .Select(s => new
            {
                s.Id,
                s.CurrentNodeId,
                s.CurrentStep,
                s.Status
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null)
        {
            throw new NotFoundException("Reading session not found.");
        }

        if (session.Status == StoryReadingStatus.Abandoned)
        {
            return;
        }

        if (session.Status == StoryReadingStatus.Completed)
        {
            throw new ConflictException("Completed reading session cannot be abandoned.");
        }

        if (session.Status != StoryReadingStatus.InProgress)
        {
            throw new ConflictException("Reading session is not active.");
        }

        var utcNow = DateTime.UtcNow;

        var rowsUpdated = await _dbContext.StoryReadingSessions
            .Where(s => s.Id == sessionId
                     && s.UserId == userId
                     && s.Status == StoryReadingStatus.InProgress
                     && s.CurrentNodeId == session.CurrentNodeId
                     && s.CurrentStep == session.CurrentStep)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, StoryReadingStatus.Abandoned)
                .SetProperty(s => s.LastReadAt, utcNow)
                .SetProperty(s => s.CompletedAt, (DateTime?)null),
                cancellationToken);

        if (rowsUpdated == 0)
        {
            var currentStatus = await _dbContext.StoryReadingSessions
                .AsNoTracking()
                .Where(s => s.Id == sessionId && s.UserId == userId)
                .Select(s => (StoryReadingStatus?)s.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (currentStatus == StoryReadingStatus.Abandoned)
            {
                return;
            }

            if (currentStatus == StoryReadingStatus.Completed)
            {
                throw new ConflictException("Completed reading session cannot be abandoned.");
            }

            throw new ConflictException("Reading session state changed concurrently.");
        }
    }

    public async Task<ReadingSessionResponse> RestartAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var sourceSession = await _dbContext.StoryReadingSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .Select(s => new
            {
                s.Id,
                s.StoryId,
                Story = new
                {
                    s.Story.Id,
                    s.Story.Title,
                    s.Story.Status,
                    s.Story.RootNodeId,
                    UniverseIsActive = s.Story.Universe.IsActive,
                    ThemeIsActive = s.Story.Universe.Theme.IsActive
                }
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (sourceSession == null)
        {
            throw new NotFoundException("Reading session not found.");
        }

        var story = sourceSession.Story;

        if (story.Status != StoryStatus.Ready || story.RootNodeId == null)
        {
            throw new ConflictException("Story is not available for restart.");
        }

        if (!story.UniverseIsActive || !story.ThemeIsActive)
        {
            throw new ConflictException("Story universe or theme is inactive.");
        }

        var rootNodeId = story.RootNodeId.Value;
        var rootNodeExists = await _dbContext.StoryNodes
            .AsNoTracking()
            .AnyAsync(n => n.Id == rootNodeId && n.StoryId == story.Id, cancellationToken);

        if (!rootNodeExists)
        {
            throw new ConflictException("Story root node is not available.");
        }

        var utcNow = DateTime.UtcNow;

        var newSession = new StoryReadingSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StoryId = story.Id,
            CurrentNodeId = rootNodeId,
            CurrentStep = 0,
            Status = StoryReadingStatus.InProgress,
            StartedAt = utcNow,
            LastReadAt = utcNow,
            CompletedAt = null
        };

        _dbContext.StoryReadingSessions.Add(newSession);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var rootNodeResponse = await ReadingResponseBuilder.BuildNodeResponseAsync(
            _dbContext,
            rootNodeId,
            cancellationToken);

        return new ReadingSessionResponse(
            newSession.Id,
            story.Id,
            story.Title,
            "InProgress",
            false,
            rootNodeResponse);
    }
}
