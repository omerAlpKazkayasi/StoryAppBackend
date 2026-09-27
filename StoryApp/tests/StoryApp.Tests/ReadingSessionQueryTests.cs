using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryApp.Application.Auth;
using StoryApp.Application.Reading;
using StoryApp.Domain.Entities;
using StoryApp.Domain.Enums;
using StoryApp.Infrastructure.Persistence;
using Xunit;

namespace StoryApp.Tests;

public class ReadingSessionQueryTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_QueryTests_{Guid.NewGuid():N}";
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

public class ReadingSessionQueryTests : IClassFixture<ReadingSessionQueryTestFixture>
{
    private readonly ReadingSessionQueryTestFixture _factory;
    private readonly HttpClient _client;

    public ReadingSessionQueryTests(ReadingSessionQueryTestFixture factory)
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

    // ==========================================
    // DETAIL / RESUME TESTS
    // ==========================================

    [Fact]
    public async Task ReadingSession_Get_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync($"/api/reading-sessions/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReadingSession_Get_WithEmptyId_ReturnsBadRequest()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/reading-sessions/{Guid.Empty}", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReadingSession_Get_ForAnotherUsersSession_ReturnsNotFound()
    {
        // Arrange: User Owner and User Other
        var (tokenOwner, ownerId) = await CreateGuestUserAsync();
        var (tokenOther, _) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Owner Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Owner Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Owner Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = ownerId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 0,
                Status = StoryReadingStatus.InProgress
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act: User Other attempts to read User Owner's session
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/reading-sessions/{sessionId}", tokenOther);
        var response = await _client.SendAsync(request);

        // Assert: 404 Not Found (no existence leak)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReadingSession_Get_UnknownSession_ReturnsNotFound()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/reading-sessions/{Guid.NewGuid()}", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReadingSession_Get_InProgress_ReturnsCurrentNode_WithScenesAndResolvedChoices()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Detail Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Detail Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Detail Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var rootNode = new StoryNode { StoryId = story.Id, Title = "Detail Root", PartNumber = 1, IsEnding = false };
        var nextNode = new StoryNode { StoryId = story.Id, Title = "Detail Next", PartNumber = 2, IsEnding = false };

        var scene1 = new StoryNodeScene { StoryNodeId = rootNode.Id, SortOrder = 1, Text = "Root Scene 1", ImageObjectKey = "img1.webp" };
        var scene2 = new StoryNodeScene { StoryNodeId = rootNode.Id, SortOrder = 2, Text = "Root Scene 2", ImageObjectKey = "img2.webp" };

        var trans1 = new StoryTransition { FromNodeId = rootNode.Id, ToNodeId = nextNode.Id, ChoiceTitle = "Advance", ChoiceIntent = "Next", SortOrder = 1 };
        var pendingTrans = new StoryTransition { FromNodeId = rootNode.Id, ToNodeId = null, ChoiceTitle = "Unresolved", ChoiceIntent = "Wait", SortOrder = 2 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [nextNode], [scene1, scene2], [trans1, pendingTrans]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 0,
                Status = StoryReadingStatus.InProgress,
                StartedAt = DateTime.UtcNow.AddHours(-1),
                LastReadAt = DateTime.UtcNow.AddMinutes(-30)
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/reading-sessions/{sessionId}", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(payload);

        Assert.Equal(sessionId, payload.SessionId);
        Assert.Equal(story.Id, payload.StoryId);
        Assert.Equal("Detail Story", payload.StoryTitle);
        Assert.Equal("InProgress", payload.Status);
        Assert.False(payload.CanGoBack); // CurrentStep == 0 -> CanGoBack = false

        Assert.Equal(rootNode.Id, payload.CurrentNode.Id);
        Assert.Equal("Detail Root", payload.CurrentNode.Title);
        Assert.False(payload.CurrentNode.IsEnding);

        // Scenes ordered by SortOrder ASC
        Assert.Equal(2, payload.CurrentNode.Scenes.Count);
        Assert.Equal("Root Scene 1", payload.CurrentNode.Scenes[0].Text);
        Assert.Equal("Root Scene 2", payload.CurrentNode.Scenes[1].Text);

        // Only resolved transitions (ToNodeId != null) included
        Assert.Single(payload.CurrentNode.Choices);
        Assert.Equal(trans1.Id, payload.CurrentNode.Choices[0].TransitionId);
        Assert.Equal("Advance", payload.CurrentNode.Choices[0].Title);
    }

    [Fact]
    public async Task ReadingSession_Get_Completed_ReturnsCompletedState_WithCanGoBackTrue()
    {
        // Arrange: Completed session with CurrentStep = 2
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Completed Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Completed Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Completed Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1, IsEnding = false };
        var endingNode = new StoryNode { StoryId = story.Id, Title = "Victory Ending", PartNumber = 2, IsEnding = true };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [endingNode]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = endingNode.Id,
                CurrentStep = 2, // Reached ending at step 2
                Status = StoryReadingStatus.Completed,
                StartedAt = DateTime.UtcNow.AddHours(-2),
                LastReadAt = DateTime.UtcNow.AddMinutes(-10),
                CompletedAt = DateTime.UtcNow.AddMinutes(-10)
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/reading-sessions/{sessionId}", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(payload);

        Assert.Equal("Completed", payload.Status);
        Assert.True(payload.CanGoBack); // CurrentStep = 2 > 0 -> true (allows Back to previous decision)
        Assert.Equal(endingNode.Id, payload.CurrentNode.Id);
        Assert.True(payload.CurrentNode.IsEnding);
        Assert.Empty(payload.CurrentNode.Choices); // Ending nodes have empty choices
    }

    [Fact]
    public async Task ReadingSession_Get_Abandoned_ReturnsAbandonedState()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Abandoned Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Abandoned Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Abandoned Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Abandoned Root", PartNumber = 1 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 0,
                Status = StoryReadingStatus.Abandoned
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act: GET should not return 409, it should return 200 with Status = Abandoned
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/reading-sessions/{sessionId}", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Abandoned", payload.Status);
    }

    [Fact]
    public async Task ReadingSession_Get_DoesNotChangeLastReadAt_NoSideEffects()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "SideEffect Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "SideEffect Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "SideEffect Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "SideEffect Root", PartNumber = 1 };

        var fixedLastReadAt = new DateTime(2026, 5, 10, 14, 30, 0, DateTimeKind.Utc);
        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 1,
                Status = StoryReadingStatus.InProgress,
                StartedAt = fixedLastReadAt.AddMinutes(-20),
                LastReadAt = fixedLastReadAt
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act: Read session via GET
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/reading-sessions/{sessionId}", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert: Database record was NOT modified
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessionInDb = await db.StoryReadingSessions.FirstAsync(s => s.Id == sessionId);
            Assert.Equal(fixedLastReadAt, sessionInDb.LastReadAt);
        }
    }

    // ==========================================
    // LIST TESTS
    // ==========================================

    [Fact]
    public async Task ReadingSessions_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/reading-sessions");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReadingSessions_ReturnsOnlyCurrentUsersSessions()
    {
        // Arrange
        var (tokenA, userA) = await CreateGuestUserAsync();
        var (_, userB) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Isolation Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Isolation Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Isolation Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Isolation Root", PartNumber = 1 };

        Guid sessionA = Guid.NewGuid();
        Guid sessionB = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode);

            var sA = new StoryReadingSession
            {
                Id = sessionA,
                UserId = userA,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 0,
                Status = StoryReadingStatus.InProgress,
                LastReadAt = DateTime.UtcNow
            };

            var sB = new StoryReadingSession
            {
                Id = sessionB,
                UserId = userB,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 0,
                Status = StoryReadingStatus.InProgress,
                LastReadAt = DateTime.UtcNow
            };

            db.StoryReadingSessions.AddRange(sA, sB);
            await db.SaveChangesAsync();
        }

        // Act: User A requests their session list
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/reading-sessions", tokenA);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listResponse = await response.Content.ReadFromJsonAsync<ReadingSessionListResponse>();
        Assert.NotNull(listResponse);

        // Should contain User A's session, but NOT User B's session
        Assert.Contains(listResponse.Items, s => s.SessionId == sessionA);
        Assert.DoesNotContain(listResponse.Items, s => s.SessionId == sessionB);
    }

    [Fact]
    public async Task ReadingSessions_ReturnsEmptyListWhenNoneExist()
    {
        // Arrange: New user with no reading sessions
        var (token, _) = await CreateGuestUserAsync();

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/reading-sessions", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listResponse = await response.Content.ReadFromJsonAsync<ReadingSessionListResponse>();
        Assert.NotNull(listResponse);

        Assert.Empty(listResponse.Items);
        Assert.Equal(0, listResponse.TotalCount);
        Assert.Equal(0, listResponse.TotalPages);
        Assert.Equal(1, listResponse.Page);
        Assert.Equal(20, listResponse.PageSize);
    }

    [Fact]
    public async Task ReadingSessions_OrdersByLastReadAtDescending_WithDeterministicTieBreaker()
    {
        // Arrange: User with 3 sessions with distinct LastReadAt timestamps
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Ordering Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Ordering Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Ordering Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Ordering Root", PartNumber = 1 };

        var now = DateTime.UtcNow;
        var s1Id = Guid.NewGuid();
        var s2Id = Guid.NewGuid();
        var s3Id = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode);

            var s1 = new StoryReadingSession { Id = s1Id, UserId = userId, StoryId = story.Id, CurrentNodeId = rootNode.Id, Status = StoryReadingStatus.InProgress, LastReadAt = now.AddMinutes(-30) };
            var s2 = new StoryReadingSession { Id = s2Id, UserId = userId, StoryId = story.Id, CurrentNodeId = rootNode.Id, Status = StoryReadingStatus.InProgress, LastReadAt = now.AddMinutes(-5) }; // Latest
            var s3 = new StoryReadingSession { Id = s3Id, UserId = userId, StoryId = story.Id, CurrentNodeId = rootNode.Id, Status = StoryReadingStatus.InProgress, LastReadAt = now.AddMinutes(-60) }; // Oldest

            db.StoryReadingSessions.AddRange(s1, s2, s3);
            await db.SaveChangesAsync();
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/reading-sessions", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listResponse = await response.Content.ReadFromJsonAsync<ReadingSessionListResponse>();
        Assert.NotNull(listResponse);

        var userItems = listResponse.Items.Where(s => s.SessionId == s1Id || s.SessionId == s2Id || s.SessionId == s3Id).ToList();
        Assert.Equal(3, userItems.Count);

        // Expected order: s2 (-5 min) -> s1 (-30 min) -> s3 (-60 min)
        Assert.Equal(s2Id, userItems[0].SessionId);
        Assert.Equal(s1Id, userItems[1].SessionId);
        Assert.Equal(s3Id, userItems[2].SessionId);
    }

    [Fact]
    public async Task ReadingSessions_FiltersByStatus()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Filter Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Filter Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Filter Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Filter Root", PartNumber = 1 };

        var inProgressId = Guid.NewGuid();
        var completedId = Guid.NewGuid();
        var abandonedId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode);

            var s1 = new StoryReadingSession { Id = inProgressId, UserId = userId, StoryId = story.Id, CurrentNodeId = rootNode.Id, Status = StoryReadingStatus.InProgress, LastReadAt = DateTime.UtcNow };
            var s2 = new StoryReadingSession { Id = completedId, UserId = userId, StoryId = story.Id, CurrentNodeId = rootNode.Id, Status = StoryReadingStatus.Completed, LastReadAt = DateTime.UtcNow.AddMinutes(-1), CompletedAt = DateTime.UtcNow };
            var s3 = new StoryReadingSession { Id = abandonedId, UserId = userId, StoryId = story.Id, CurrentNodeId = rootNode.Id, Status = StoryReadingStatus.Abandoned, LastReadAt = DateTime.UtcNow.AddMinutes(-2) };

            db.StoryReadingSessions.AddRange(s1, s2, s3);
            await db.SaveChangesAsync();
        }

        // Act 1: Filter Completed
        using var reqCompleted = CreateAuthenticatedRequest(HttpMethod.Get, "/api/reading-sessions?status=Completed", token);
        var resCompleted = await _client.SendAsync(reqCompleted);
        Assert.Equal(HttpStatusCode.OK, resCompleted.StatusCode);
        var payloadCompleted = await resCompleted.Content.ReadFromJsonAsync<ReadingSessionListResponse>();
        Assert.NotNull(payloadCompleted);
        Assert.All(payloadCompleted.Items, item => Assert.Equal("Completed", item.Status));
        Assert.Contains(payloadCompleted.Items, item => item.SessionId == completedId);
        Assert.DoesNotContain(payloadCompleted.Items, item => item.SessionId == inProgressId);

        // Act 2: Filter InProgress
        using var reqInProgress = CreateAuthenticatedRequest(HttpMethod.Get, "/api/reading-sessions?status=InProgress", token);
        var resInProgress = await _client.SendAsync(reqInProgress);
        Assert.Equal(HttpStatusCode.OK, resInProgress.StatusCode);
        var payloadInProgress = await resInProgress.Content.ReadFromJsonAsync<ReadingSessionListResponse>();
        Assert.NotNull(payloadInProgress);
        Assert.All(payloadInProgress.Items, item => Assert.Equal("InProgress", item.Status));
        Assert.Contains(payloadInProgress.Items, item => item.SessionId == inProgressId);

        // Act 3: Filter Abandoned
        using var reqAbandoned = CreateAuthenticatedRequest(HttpMethod.Get, "/api/reading-sessions?status=Abandoned", token);
        var resAbandoned = await _client.SendAsync(reqAbandoned);
        Assert.Equal(HttpStatusCode.OK, resAbandoned.StatusCode);
        var payloadAbandoned = await resAbandoned.Content.ReadFromJsonAsync<ReadingSessionListResponse>();
        Assert.NotNull(payloadAbandoned);
        Assert.All(payloadAbandoned.Items, item => Assert.Equal("Abandoned", item.Status));
        Assert.Contains(payloadAbandoned.Items, item => item.SessionId == abandonedId);
    }

    [Fact]
    public async Task ReadingSessions_PaginatesCorrectly()
    {
        // Arrange: User with 5 sessions, page size = 2
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Paging Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Paging Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Paging Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Paging Root", PartNumber = 1 };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode);

            for (int i = 1; i <= 5; i++)
            {
                var s = new StoryReadingSession
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    StoryId = story.Id,
                    CurrentNodeId = rootNode.Id,
                    Status = StoryReadingStatus.InProgress,
                    LastReadAt = DateTime.UtcNow.AddMinutes(-i)
                };
                db.StoryReadingSessions.Add(s);
            }
            await db.SaveChangesAsync();
        }

        // Act: Page 1, PageSize 2
        using var req1 = CreateAuthenticatedRequest(HttpMethod.Get, "/api/reading-sessions?page=1&pageSize=2", token);
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var p1 = await res1.Content.ReadFromJsonAsync<ReadingSessionListResponse>();
        Assert.NotNull(p1);
        Assert.Equal(1, p1.Page);
        Assert.Equal(2, p1.PageSize);
        Assert.Equal(5, p1.TotalCount);
        Assert.Equal(3, p1.TotalPages); // ceil(5 / 2) = 3
        Assert.Equal(2, p1.Items.Count);

        // Act: Page 3, PageSize 2
        using var req3 = CreateAuthenticatedRequest(HttpMethod.Get, "/api/reading-sessions?page=3&pageSize=2", token);
        var res3 = await _client.SendAsync(req3);
        Assert.Equal(HttpStatusCode.OK, res3.StatusCode);
        var p3 = await res3.Content.ReadFromJsonAsync<ReadingSessionListResponse>();
        Assert.NotNull(p3);
        Assert.Equal(3, p3.Page);
        Assert.Single(p3.Items); // 5th item
    }

    [Theory]
    [InlineData(0, 20)]   // page < 1
    [InlineData(-1, 20)]  // page < 1
    [InlineData(1, 0)]    // pageSize < 1
    [InlineData(1, -5)]   // pageSize < 1
    [InlineData(1, 101)]  // pageSize > 100
    public async Task ReadingSessions_WithInvalidPagination_ReturnsBadRequest(int page, int pageSize)
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/reading-sessions?page={page}&pageSize={pageSize}", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReadingSessions_WithInvalidStatus_ReturnsBadRequest()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/reading-sessions?status=NonExistentStatus", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReadingSessions_DoesNotExposeCurrentNodeGraph()
    {
        // Arrange: Verify that list item does not include scenes, choices, or memory graph
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Graph Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Graph Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Graph Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Graph Root", PartNumber = 1 };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode);

            var s = new StoryReadingSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                Status = StoryReadingStatus.InProgress,
                LastReadAt = DateTime.UtcNow
            };
            db.StoryReadingSessions.Add(s);
            await db.SaveChangesAsync();
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/reading-sessions", token);
        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        // Assert: Heavy graph fields must not exist in list response
        Assert.DoesNotContain("scenes", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("choices", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memory", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("currentNode", json, StringComparison.OrdinalIgnoreCase);
    }
}
