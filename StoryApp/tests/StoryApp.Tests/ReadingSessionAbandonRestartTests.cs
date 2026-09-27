using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryApp.Application.Auth;
using StoryApp.Application.Profile;
using StoryApp.Application.Reading;
using StoryApp.Domain.Entities;
using StoryApp.Domain.Enums;
using StoryApp.Infrastructure.Persistence;
using Xunit;

namespace StoryApp.Tests;

public class ReadingSessionAbandonRestartTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_AbandonRestartTests_{Guid.NewGuid():N}";
    public string ConnectionString => $"Server=(localdb)\\MSSQLLocalDB;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;";
    public const string TestSigningKey = "test_signing_key_at_least_256_bits_long_for_hmac_sha256_security_12345!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Jwt:Issuer", "StoryApp.Test");
        builder.UseSetting("Jwt:Audience", "StoryApp.TestMobile");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "15");
        builder.UseSetting("Jwt:RefreshTokenDays", "30");
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.EnsureDeletedAsync();
        }
        catch
        {
            // Ignore cleanup errors during test fixture dispose
        }

        await base.DisposeAsync();
    }
}

public class ReadingSessionAbandonRestartTests : IClassFixture<ReadingSessionAbandonRestartTestFixture>
{
    private readonly ReadingSessionAbandonRestartTestFixture _factory;
    private readonly HttpClient _client;

    public ReadingSessionAbandonRestartTests(ReadingSessionAbandonRestartTestFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(string AccessToken, Guid UserId)> CreateGuestUserAsync()
    {
        var response = await _client.PostAsync("/api/auth/guest", null);
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        return (auth!.AccessToken, auth.User.Id);
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task SaveStoryGraphAsync(
        AppDbContext db,
        Theme theme,
        StoryUniverse universe,
        Story story,
        StoryNode rootNode,
        IEnumerable<StoryNode>? otherNodes = null,
        IEnumerable<StoryNodeScene>? scenes = null,
        IEnumerable<StoryTransition>? transitions = null)
    {
        universe.Description ??= "Test Universe Description";
        db.Themes.Add(theme);
        db.StoryUniverses.Add(universe);
        story.RootNodeId = null;
        db.Stories.Add(story);
        db.StoryNodes.Add(rootNode);

        if (otherNodes != null)
        {
            db.StoryNodes.AddRange(otherNodes);
        }

        if (scenes != null)
        {
            db.StoryNodeScenes.AddRange(scenes);
        }

        if (transitions != null)
        {
            db.StoryTransitions.AddRange(transitions);
        }

        await db.SaveChangesAsync();

        story.RootNodeId = rootNode.Id;
        await db.SaveChangesAsync();
    }

    private async Task<(Guid SessionId, Guid StoryId, Guid RootNodeId)> CreateTestSessionAsync(
        Guid userId,
        StoryReadingStatus status = StoryReadingStatus.InProgress,
        int currentStep = 0,
        bool isEnding = false,
        string language = "tr",
        int childAge = 8,
        bool universeActive = true,
        bool themeActive = true,
        StoryStatus storyStatus = StoryStatus.Ready)
    {
        var theme = new Theme { Name = $"Theme_{Guid.NewGuid():N}", IsActive = themeActive };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = $"Universe_{Guid.NewGuid():N}", IsActive = universeActive, MinimumAge = 3, MaximumAge = 12 };
        var story = new Story { UniverseId = universe.Id, Title = $"Story_{Guid.NewGuid():N}", Status = storyStatus, Language = language, ChildAge = childAge };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1, IsEnding = isEnding };
        var targetNode = new StoryNode { StoryId = story.Id, Title = "Target Node", PartNumber = 2, IsEnding = true };
        var scene = new StoryNodeScene { StoryNodeId = rootNode.Id, SortOrder = 1, Text = "Root scene text" };
        var transition = new StoryTransition { FromNodeId = rootNode.Id, ToNodeId = targetNode.Id, ChoiceTitle = "Choice 1", ChoiceIntent = "Go", SortOrder = 1 };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [targetNode], [scene], [transition]);

        var session = new StoryReadingSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StoryId = story.Id,
            CurrentNodeId = rootNode.Id,
            CurrentStep = currentStep,
            Status = status,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            LastReadAt = DateTime.UtcNow.AddMinutes(-5),
            CompletedAt = status == StoryReadingStatus.Completed ? DateTime.UtcNow.AddMinutes(-2) : null
        };
        db.StoryReadingSessions.Add(session);

        if (currentStep > 0)
        {
            db.StoryReadingHistories.Add(new StoryReadingHistory
            {
                Id = Guid.NewGuid(),
                ReadingSessionId = session.Id,
                StepNumber = 1,
                FromNodeId = rootNode.Id,
                TransitionId = transition.Id,
                ToNodeId = targetNode.Id,
                SelectedAt = DateTime.UtcNow.AddMinutes(-4)
            });
        }

        await db.SaveChangesAsync();

        return (session.Id, story.Id, rootNode.Id);
    }

    #region Abandon Tests

    [Fact]
    public async Task Abandon_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.PostAsync($"/api/reading-sessions/{Guid.NewGuid()}/abandon", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Abandon_WithEmptySessionId_ReturnsBadRequest()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{Guid.Empty}/abandon", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Abandon_ForAnotherUsersSession_ReturnsNotFound()
    {
        // Arrange
        var (tokenOwner, ownerId) = await CreateGuestUserAsync();
        var (tokenOther, _) = await CreateGuestUserAsync();

        var (sessionId, _, _) = await CreateTestSessionAsync(ownerId);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/abandon", tokenOther);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Abandon_UnknownSession_ReturnsNotFound()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{Guid.NewGuid()}/abandon", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Abandon_InProgressSession_ReturnsNoContent_AndSetsStatusToAbandoned()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, _, rootNodeId) = await CreateTestSessionAsync(userId, StoryReadingStatus.InProgress, currentStep: 0);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/abandon", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.StoryReadingSessions.FindAsync(sessionId);

        Assert.NotNull(session);
        Assert.Equal(StoryReadingStatus.Abandoned, session.Status);
        Assert.Equal(rootNodeId, session.CurrentNodeId);
        Assert.Equal(0, session.CurrentStep);
        Assert.Null(session.CompletedAt);
    }

    [Fact]
    public async Task Abandon_UpdatesLastReadAt()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, _, _) = await CreateTestSessionAsync(userId, StoryReadingStatus.InProgress);

        DateTime previousLastReadAt;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.StoryReadingSessions.FindAsync(sessionId);
            previousLastReadAt = s!.LastReadAt;
        }

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/abandon", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.StoryReadingSessions.FindAsync(sessionId);
            Assert.NotNull(session);
            Assert.True(session.LastReadAt > previousLastReadAt);
        }
    }

    [Fact]
    public async Task Abandon_DoesNotDeleteHistory()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, _, _) = await CreateTestSessionAsync(userId, StoryReadingStatus.InProgress, currentStep: 1);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/abandon", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var histories = await db.StoryReadingHistories.Where(h => h.ReadingSessionId == sessionId).ToListAsync();

        Assert.Single(histories);
    }

    [Fact]
    public async Task Abandon_CompletedSession_ReturnsConflict()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, _, _) = await CreateTestSessionAsync(userId, StoryReadingStatus.Completed);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/abandon", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.StoryReadingSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal(StoryReadingStatus.Completed, session.Status);
    }

    [Fact]
    public async Task Abandon_AlreadyAbandoned_IsIdempotent_ReturnsNoContent()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, _, _) = await CreateTestSessionAsync(userId, StoryReadingStatus.Abandoned);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/abandon", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.StoryReadingSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal(StoryReadingStatus.Abandoned, session.Status);
    }

    [Fact]
    public async Task Abandon_ConcurrentDoubleSubmit_RemainsAbandoned()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, _, _) = await CreateTestSessionAsync(userId, StoryReadingStatus.InProgress);

        // Act: Concurrent requests
        using var req1 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/abandon", token);
        using var req2 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/abandon", token);

        var task1 = _client.SendAsync(req1);
        var task2 = _client.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);

        // Assert: At least one is 204 NoContent, both could be 204 due to idempotency
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.StoryReadingSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal(StoryReadingStatus.Abandoned, session.Status);
    }

    #endregion

    #region Restart Tests

    [Fact]
    public async Task Restart_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.PostAsync($"/api/reading-sessions/{Guid.NewGuid()}/restart", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Restart_WithEmptySessionId_ReturnsBadRequest()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{Guid.Empty}/restart", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Restart_ForAnotherUsersSession_ReturnsNotFound()
    {
        // Arrange
        var (tokenOwner, ownerId) = await CreateGuestUserAsync();
        var (tokenOther, _) = await CreateGuestUserAsync();

        var (sessionId, _, _) = await CreateTestSessionAsync(ownerId);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/restart", tokenOther);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Restart_UnknownSession_ReturnsNotFound()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{Guid.NewGuid()}/restart", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Restart_FromInProgress_CreatesNewSession()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, storyId, rootNodeId) = await CreateTestSessionAsync(userId, StoryReadingStatus.InProgress, currentStep: 2);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/restart", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(sessionId, result.SessionId);
        Assert.Equal(storyId, result.StoryId);
        Assert.Equal("InProgress", result.Status);
        Assert.False(result.CanGoBack);
        Assert.Equal(rootNodeId, result.CurrentNode.Id);

        // Verify in DB: Old session intact
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var oldSession = await db.StoryReadingSessions.FindAsync(sessionId);
        Assert.NotNull(oldSession);
        Assert.Equal(StoryReadingStatus.InProgress, oldSession.Status);
        Assert.Equal(2, oldSession.CurrentStep);

        // New session created
        var newSession = await db.StoryReadingSessions.FindAsync(result.SessionId);
        Assert.NotNull(newSession);
        Assert.Equal(storyId, newSession.StoryId);
        Assert.Equal(rootNodeId, newSession.CurrentNodeId);
        Assert.Equal(0, newSession.CurrentStep);
        Assert.Equal(StoryReadingStatus.InProgress, newSession.Status);
    }

    [Fact]
    public async Task Restart_FromCompleted_CreatesNewSession()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, storyId, rootNodeId) = await CreateTestSessionAsync(userId, StoryReadingStatus.Completed, currentStep: 3);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/restart", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(sessionId, result.SessionId);
        Assert.Equal(storyId, result.StoryId);
        Assert.Equal("InProgress", result.Status);
        Assert.False(result.CanGoBack);
        Assert.Equal(rootNodeId, result.CurrentNode.Id);

        // Old session remains completed
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var oldSession = await db.StoryReadingSessions.FindAsync(sessionId);
        Assert.NotNull(oldSession);
        Assert.Equal(StoryReadingStatus.Completed, oldSession.Status);
        Assert.NotNull(oldSession.CompletedAt);
    }

    [Fact]
    public async Task Restart_FromAbandoned_CreatesNewSession()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, storyId, rootNodeId) = await CreateTestSessionAsync(userId, StoryReadingStatus.Abandoned, currentStep: 1);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/restart", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(sessionId, result.SessionId);
        Assert.Equal(storyId, result.StoryId);
        Assert.Equal("InProgress", result.Status);
        Assert.Equal(rootNodeId, result.CurrentNode.Id);

        // Old session remains abandoned
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var oldSession = await db.StoryReadingSessions.FindAsync(sessionId);
        Assert.NotNull(oldSession);
        Assert.Equal(StoryReadingStatus.Abandoned, oldSession.Status);
    }

    [Fact]
    public async Task Restart_PreservesOldSession_AndItsHistory_AndNewSessionHasEmptyHistory()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, storyId, rootNodeId) = await CreateTestSessionAsync(userId, StoryReadingStatus.Completed, currentStep: 2);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/restart", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(result);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Old session history intact
        var oldHistories = await db.StoryReadingHistories.Where(h => h.ReadingSessionId == sessionId).ToListAsync();
        Assert.Single(oldHistories);

        // New session has NO history
        var newHistories = await db.StoryReadingHistories.Where(h => h.ReadingSessionId == result.SessionId).ToListAsync();
        Assert.Empty(newHistories);
    }

    [Fact]
    public async Task Restart_WhenStoryNotReady_ReturnsConflict()
    {
        // Arrange: Story is generating
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, _, _) = await CreateTestSessionAsync(userId, StoryReadingStatus.Completed, storyStatus: StoryStatus.Generating);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/restart", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Restart_WhenUniverseInactive_ReturnsConflict()
    {
        // Arrange: Universe is inactive
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, _, _) = await CreateTestSessionAsync(userId, StoryReadingStatus.Completed, universeActive: false);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/restart", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Restart_WhenThemeInactive_ReturnsConflict()
    {
        // Arrange: Theme is inactive
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, _, _) = await CreateTestSessionAsync(userId, StoryReadingStatus.Completed, themeActive: false);

        // Act
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/restart", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Restart_DoesNotReevaluateProfileAgeOrLanguage()
    {
        // Arrange: Story is for Age 8, Language "tr"
        var (token, userId) = await CreateGuestUserAsync();
        var (sessionId, storyId, _) = await CreateTestSessionAsync(userId, StoryReadingStatus.Completed, language: "tr", childAge: 8);

        // User profile changes to Age 12, Language "en"
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserProfiles.Add(new UserProfile
            {
                UserId = userId,
                ChildAge = 12,
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Act: Restart must still succeed because it restarts the chosen story without re-filtering
        using var req = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/restart", token);
        var response = await _client.SendAsync(req);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(result);
        Assert.Equal(storyId, result.StoryId);
        Assert.Equal("InProgress", result.Status);
    }

    #endregion
}
