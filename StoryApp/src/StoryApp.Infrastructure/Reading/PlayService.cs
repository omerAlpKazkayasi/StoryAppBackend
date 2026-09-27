using Microsoft.EntityFrameworkCore;
using StoryApp.Application.Common.Exceptions;
using StoryApp.Application.Reading;
using StoryApp.Domain.Entities;
using StoryApp.Domain.Enums;
using StoryApp.Infrastructure.Persistence;

namespace StoryApp.Infrastructure.Reading;

public sealed class PlayService : IPlayService
{
    private readonly AppDbContext _dbContext;

    public PlayService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReadingSessionResponse> StartAsync(
        Guid userId,
        Guid themeId,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate Theme
        var themeExistsAndActive = await _dbContext.Themes
            .AsNoTracking()
            .AnyAsync(t => t.Id == themeId && t.IsActive, cancellationToken);

        if (!themeExistsAndActive)
        {
            throw new NotFoundException("Theme not found or is inactive.");
        }

        // 2. Read UserProfile (if any)
        var profile = await _dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new
            {
                p.ChildAge,
                p.PreferredLanguage
            })
            .FirstOrDefaultAsync(cancellationToken);

        // 3. Playable Story candidate query
        var playableStoriesQuery = _dbContext.Stories
            .AsNoTracking()
            .Where(s => s.Universe.ThemeId == themeId
                     && s.Universe.IsActive
                     && s.Status == StoryStatus.Ready
                     && s.RootNodeId != null
                     && s.Nodes.Any(n => n.Id == s.RootNodeId && n.StoryId == s.Id));

        if (profile?.ChildAge != null)
        {
            var childAge = profile.ChildAge.Value;
            playableStoriesQuery = playableStoriesQuery.Where(s =>
                s.ChildAge == childAge
                && s.Universe.MinimumAge <= childAge
                && s.Universe.MaximumAge >= childAge);
        }

        if (!string.IsNullOrWhiteSpace(profile?.PreferredLanguage))
        {
            var preferredLanguage = profile.PreferredLanguage;
            playableStoriesQuery = playableStoriesQuery.Where(s => s.Language == preferredLanguage);
        }

        // 4. Repeat avoidance: prefer unread stories (not completed by this user)
        var unreadStoriesQuery = playableStoriesQuery
            .Where(s => !_dbContext.StoryReadingSessions.Any(rs =>
                rs.UserId == userId
                && rs.StoryId == s.Id
                && rs.Status == StoryReadingStatus.Completed));

        var unreadCount = await unreadStoriesQuery.CountAsync(cancellationToken);
        IQueryable<Story> selectedCandidateQuery;

        if (unreadCount > 0)
        {
            var randomIndex = Random.Shared.Next(unreadCount);
            selectedCandidateQuery = unreadStoriesQuery
                .OrderBy(s => s.Id)
                .Skip(randomIndex);
        }
        else
        {
            var totalPlayableCount = await playableStoriesQuery.CountAsync(cancellationToken);
            if (totalPlayableCount == 0)
            {
                throw new ConflictException("No playable story is currently available for this theme.");
            }

            var randomIndex = Random.Shared.Next(totalPlayableCount);
            selectedCandidateQuery = playableStoriesQuery
                .OrderBy(s => s.Id)
                .Skip(randomIndex);
        }

        var selectedStory = await selectedCandidateQuery
            .Select(s => new
            {
                s.Id,
                s.Title,
                RootNodeId = s.RootNodeId!.Value
            })
            .FirstAsync(cancellationToken);

        // 4. Create Reading Session
        var utcNow = DateTime.UtcNow;
        var session = new StoryReadingSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StoryId = selectedStory.Id,
            CurrentNodeId = selectedStory.RootNodeId,
            CurrentStep = 0,
            Status = StoryReadingStatus.InProgress,
            StartedAt = utcNow,
            LastReadAt = utcNow,
            CompletedAt = null
        };

        _dbContext.StoryReadingSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 5. Build Root Node details (Scenes and Choices)
        var currentNodeResponse = await ReadingResponseBuilder.BuildNodeResponseAsync(
            _dbContext,
            selectedStory.RootNodeId,
            cancellationToken);

        return new ReadingSessionResponse(
            session.Id,
            selectedStory.Id,
            selectedStory.Title,
            "InProgress",
            false,
            currentNodeResponse);
    }
}
