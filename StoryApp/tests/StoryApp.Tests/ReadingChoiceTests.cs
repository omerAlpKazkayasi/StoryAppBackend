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

public class ReadingChoiceTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_ChoiceTests_{Guid.NewGuid():N}";
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

public class ReadingChoiceTests : IClassFixture<ReadingChoiceTestFixture>
{
    private readonly ReadingChoiceTestFixture _factory;
    private readonly HttpClient _client;

    public ReadingChoiceTests(ReadingChoiceTestFixture factory)
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

    [Fact]
    public async Task Choice_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.PostAsJsonAsync($"/api/reading-sessions/{Guid.NewGuid()}/choices", new ChoiceRequest(Guid.NewGuid(), Guid.NewGuid()));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Choice_WithEmptyIds_ReturnsBadRequest()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act 1: Empty route SessionId
        using var req1 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{Guid.Empty}/choices", token);
        req1.Content = JsonContent.Create(new ChoiceRequest(Guid.NewGuid(), Guid.NewGuid()));
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.BadRequest, res1.StatusCode);

        // Act 2: Empty CurrentNodeId
        using var req2 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{Guid.NewGuid()}/choices", token);
        req2.Content = JsonContent.Create(new ChoiceRequest(Guid.Empty, Guid.NewGuid()));
        var res2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);

        // Act 3: Empty TransitionId
        using var req3 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{Guid.NewGuid()}/choices", token);
        req3.Content = JsonContent.Create(new ChoiceRequest(Guid.NewGuid(), Guid.Empty));
        var res3 = await _client.SendAsync(req3);
        Assert.Equal(HttpStatusCode.BadRequest, res3.StatusCode);
    }

    [Fact]
    public async Task Choice_ForAnotherUsersSession_ReturnsNotFound()
    {
        // Arrange
        var (tokenOwner, userOwnerId) = await CreateGuestUserAsync();
        var (tokenOther, _) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Owner Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Owner Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Owner Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };
        var targetNode = new StoryNode { StoryId = story.Id, Title = "Target Node", PartNumber = 2 };
        var transition = new StoryTransition { FromNodeId = rootNode.Id, ToNodeId = targetNode.Id, ChoiceTitle = "Choice", ChoiceIntent = "Go", SortOrder = 1 };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [targetNode], transitions: [transition]);

            var session = new StoryReadingSession
            {
                Id = Guid.NewGuid(),
                UserId = userOwnerId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 0,
                Status = StoryReadingStatus.InProgress
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();

            // Act: userOther tries to make choice on owner's session
            using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{session.Id}/choices", tokenOther);
            request.Content = JsonContent.Create(new ChoiceRequest(rootNode.Id, transition.Id));
            var response = await _client.SendAsync(request);

            // Assert: Must return 404 Not Found (no disclosure)
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Choice_OnCompletedOrAbandonedSession_ReturnsConflict()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Status Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Status Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Status Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };
        var targetNode = new StoryNode { StoryId = story.Id, Title = "Target Node", PartNumber = 2 };
        var transition = new StoryTransition { FromNodeId = rootNode.Id, ToNodeId = targetNode.Id, ChoiceTitle = "Go", ChoiceIntent = "Intent", SortOrder = 1 };

        Guid completedSessionId = Guid.NewGuid();
        Guid abandonedSessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [targetNode], transitions: [transition]);

            var completedSession = new StoryReadingSession
            {
                Id = completedSessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 1,
                Status = StoryReadingStatus.Completed
            };
            var abandonedSession = new StoryReadingSession
            {
                Id = abandonedSessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 1,
                Status = StoryReadingStatus.Abandoned
            };
            db.StoryReadingSessions.AddRange(completedSession, abandonedSession);
            await db.SaveChangesAsync();
        }

        // Act 1: Choice on completed session
        using var req1 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{completedSessionId}/choices", token);
        req1.Content = JsonContent.Create(new ChoiceRequest(rootNode.Id, transition.Id));
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Conflict, res1.StatusCode);

        // Act 2: Choice on abandoned session
        using var req2 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{abandonedSessionId}/choices", token);
        req2.Content = JsonContent.Create(new ChoiceRequest(rootNode.Id, transition.Id));
        var res2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }

    [Fact]
    public async Task Choice_WithStaleCurrentNode_ReturnsConflict()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Stale Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Stale Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Stale Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };
        var targetNode = new StoryNode { StoryId = story.Id, Title = "Target Node", PartNumber = 2 };
        var otherNode = new StoryNode { StoryId = story.Id, Title = "Other Node", PartNumber = 3 };
        var transition = new StoryTransition { FromNodeId = rootNode.Id, ToNodeId = targetNode.Id, ChoiceTitle = "Go", ChoiceIntent = "Intent", SortOrder = 1 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [targetNode, otherNode], transitions: [transition]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id, // Session is currently at rootNode
                CurrentStep = 0,
                Status = StoryReadingStatus.InProgress
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act: send currentNodeId = otherNode.Id (does not match session.CurrentNodeId)
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/choices", token);
        request.Content = JsonContent.Create(new ChoiceRequest(otherNode.Id, transition.Id));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Choice_WithInvalidOrPendingTransition_ReturnsConflict()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Invalid Transition Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Invalid Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Main Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var otherStory = new Story { UniverseId = universe.Id, Title = "Other Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };
        var otherNode = new StoryNode { StoryId = story.Id, Title = "Other Node", PartNumber = 2 };
        var externalNode = new StoryNode { StoryId = otherStory.Id, Title = "External Node", PartNumber = 1 };

        // Transition from other node
        var transFromOtherNode = new StoryTransition { FromNodeId = otherNode.Id, ToNodeId = rootNode.Id, ChoiceTitle = "Back", ChoiceIntent = "Back", SortOrder = 1 };

        // Transition with ToNodeId == null (Pending)
        var transPending = new StoryTransition { FromNodeId = rootNode.Id, ToNodeId = null, ChoiceTitle = "Pending", ChoiceIntent = "Wait", SortOrder = 2 };

        // Transition from external story node
        var transExternal = new StoryTransition { FromNodeId = externalNode.Id, ToNodeId = rootNode.Id, ChoiceTitle = "Alien", ChoiceIntent = "Alien", SortOrder = 3 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [otherNode]);

            otherStory.RootNodeId = null;
            db.Stories.Add(otherStory);
            db.StoryNodes.Add(externalNode);
            db.StoryTransitions.AddRange(transFromOtherNode, transPending, transExternal);
            await db.SaveChangesAsync();

            otherStory.RootNodeId = externalNode.Id;

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 0,
                Status = StoryReadingStatus.InProgress
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Test 1: Transition from another node
        using var req1 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/choices", token);
        req1.Content = JsonContent.Create(new ChoiceRequest(rootNode.Id, transFromOtherNode.Id));
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Conflict, res1.StatusCode);

        // Test 2: Pending transition (ToNodeId == null)
        using var req2 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/choices", token);
        req2.Content = JsonContent.Create(new ChoiceRequest(rootNode.Id, transPending.Id));
        var res2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);

        // Test 3: Transition from another story
        using var req3 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/choices", token);
        req3.Content = JsonContent.Create(new ChoiceRequest(rootNode.Id, transExternal.Id));
        var res3 = await _client.SendAsync(req3);
        Assert.Equal(HttpStatusCode.Conflict, res3.StatusCode);
    }

    [Fact]
    public async Task Choice_WithValidTransition_MovesToTargetNode_AndIncrementsStep_AndWritesHistory()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Valid Choice Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Valid Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Great Adventure", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1, IsEnding = false };
        var targetNode = new StoryNode { StoryId = story.Id, Title = "Forest Glade", PartNumber = 2, IsEnding = false };
        var nextNode = new StoryNode { StoryId = story.Id, Title = "Dark Cave", PartNumber = 3, IsEnding = false };

        var rootToTargetTransition = new StoryTransition
        {
            FromNodeId = rootNode.Id,
            ToNodeId = targetNode.Id,
            ChoiceTitle = "Walk to the glade",
            ChoiceIntent = "Walk",
            SortOrder = 1
        };

        var targetScene1 = new StoryNodeScene { StoryNodeId = targetNode.Id, SortOrder = 2, Text = "Glade Scene 2", ImageObjectKey = "img-glade-2" };
        var targetScene2 = new StoryNodeScene { StoryNodeId = targetNode.Id, SortOrder = 1, Text = "Glade Scene 1", ImageObjectKey = "img-glade-1" };

        var targetChoice1 = new StoryTransition { FromNodeId = targetNode.Id, ToNodeId = nextNode.Id, ChoiceTitle = "Enter Dark Cave", ChoiceIntent = "Enter", SortOrder = 1 };
        var targetChoicePending = new StoryTransition { FromNodeId = targetNode.Id, ToNodeId = null, ChoiceTitle = "Pending Cave Choice", ChoiceIntent = "Wait", SortOrder = 2 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(
                db,
                theme,
                universe,
                story,
                rootNode,
                otherNodes: [targetNode, nextNode],
                scenes: [targetScene1, targetScene2],
                transitions: [rootToTargetTransition, targetChoice1, targetChoicePending]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 0,
                Status = StoryReadingStatus.InProgress,
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                LastReadAt = DateTime.UtcNow.AddMinutes(-5)
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/choices", token);
        request.Content = JsonContent.Create(new ChoiceRequest(rootNode.Id, rootToTargetTransition.Id));
        var response = await _client.SendAsync(request);

        // Assert HTTP response
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var readingResponse = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(readingResponse);

        // Contract assertions
        Assert.Equal(sessionId, readingResponse.SessionId);
        Assert.Equal(story.Id, readingResponse.StoryId);
        Assert.Equal("Great Adventure", readingResponse.StoryTitle);
        Assert.Equal("InProgress", readingResponse.Status);
        Assert.True(readingResponse.CanGoBack); // CurrentStep >= 1 -> CanGoBack = true

        // CurrentNode assertions (Target node)
        Assert.Equal(targetNode.Id, readingResponse.CurrentNode.Id);
        Assert.Equal("Forest Glade", readingResponse.CurrentNode.Title);
        Assert.False(readingResponse.CurrentNode.IsEnding);

        // Target scenes ordered by SortOrder ASC
        Assert.Equal(2, readingResponse.CurrentNode.Scenes.Count);
        Assert.Equal(1, readingResponse.CurrentNode.Scenes[0].SortOrder);
        Assert.Equal("Glade Scene 1", readingResponse.CurrentNode.Scenes[0].Text);
        Assert.Equal(2, readingResponse.CurrentNode.Scenes[1].SortOrder);
        Assert.Equal("Glade Scene 2", readingResponse.CurrentNode.Scenes[1].Text);

        // Target choices ordered by SortOrder ASC, excludes pending
        Assert.Single(readingResponse.CurrentNode.Choices);
        Assert.Equal(targetChoice1.Id, readingResponse.CurrentNode.Choices[0].TransitionId);
        Assert.Equal("Enter Dark Cave", readingResponse.CurrentNode.Choices[0].Title);
        Assert.Equal(1, readingResponse.CurrentNode.Choices[0].SortOrder);

        // Database assertions
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.StoryReadingSessions.FirstAsync(s => s.Id == sessionId);
            Assert.Equal(targetNode.Id, session.CurrentNodeId);
            Assert.Equal(1, session.CurrentStep);
            Assert.Equal(StoryReadingStatus.InProgress, session.Status);
            Assert.Null(session.CompletedAt);

            var histories = await db.StoryReadingHistories.Where(h => h.ReadingSessionId == sessionId).ToListAsync();
            Assert.Single(histories);
            var history = histories[0];
            Assert.Equal(1, history.StepNumber);
            Assert.Equal(rootNode.Id, history.FromNodeId);
            Assert.Equal(rootToTargetTransition.Id, history.TransitionId);
            Assert.Equal(targetNode.Id, history.ToNodeId);
        }
    }

    [Fact]
    public async Task Choice_ToEnding_CompletesSession_AndSetsCompletedAt_AndReturnsEmptyChoices()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Ending Choice Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Ending Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Ending Tale", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1, IsEnding = false };
        var endingNode = new StoryNode { StoryId = story.Id, Title = "The Happy End", PartNumber = 2, IsEnding = true };

        var transitionToEnding = new StoryTransition
        {
            FromNodeId = rootNode.Id,
            ToNodeId = endingNode.Id,
            ChoiceTitle = "Complete journey",
            ChoiceIntent = "End",
            SortOrder = 1
        };

        // Accidental transition from ending node in DB (must be suppressed by IsEnding == true)
        var ghostTransition = new StoryTransition
        {
            FromNodeId = endingNode.Id,
            ToNodeId = rootNode.Id,
            ChoiceTitle = "Ghost loop",
            ChoiceIntent = "Ghost",
            SortOrder = 1
        };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(
                db,
                theme,
                universe,
                story,
                rootNode,
                otherNodes: [endingNode],
                transitions: [transitionToEnding, ghostTransition]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 0,
                Status = StoryReadingStatus.InProgress
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/choices", token);
        request.Content = JsonContent.Create(new ChoiceRequest(rootNode.Id, transitionToEnding.Id));
        var response = await _client.SendAsync(request);

        // Assert HTTP response
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var readingResponse = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(readingResponse);

        Assert.Equal("Completed", readingResponse.Status);
        Assert.True(readingResponse.CanGoBack);
        Assert.True(readingResponse.CurrentNode.IsEnding);
        Assert.Empty(readingResponse.CurrentNode.Choices);

        // Database assertions
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.StoryReadingSessions.FirstAsync(s => s.Id == sessionId);
            Assert.Equal(endingNode.Id, session.CurrentNodeId);
            Assert.Equal(1, session.CurrentStep);
            Assert.Equal(StoryReadingStatus.Completed, session.Status);
            Assert.NotNull(session.CompletedAt);
        }
    }

    [Fact]
    public async Task Choice_AfterRewind_RemovesFutureHistory()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Rewind Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Rewind Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Rewind Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var node1 = new StoryNode { StoryId = story.Id, Title = "Node 1 (Root)", PartNumber = 1 };
        var node2Old = new StoryNode { StoryId = story.Id, Title = "Node 2 (Old Branch)", PartNumber = 2 };
        var node3Old = new StoryNode { StoryId = story.Id, Title = "Node 3 (Old Branch)", PartNumber = 3 };
        var node2New = new StoryNode { StoryId = story.Id, Title = "Node 2 (New Branch)", PartNumber = 4 };

        var transOld1To2 = new StoryTransition { FromNodeId = node1.Id, ToNodeId = node2Old.Id, ChoiceTitle = "Old 1->2", ChoiceIntent = "Go", SortOrder = 1 };
        var transOld2To3 = new StoryTransition { FromNodeId = node2Old.Id, ToNodeId = node3Old.Id, ChoiceTitle = "Old 2->3", ChoiceIntent = "Go", SortOrder = 1 };
        var transNew1To2 = new StoryTransition { FromNodeId = node1.Id, ToNodeId = node2New.Id, ChoiceTitle = "New 1->2", ChoiceIntent = "Go", SortOrder = 2 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(
                db,
                theme,
                universe,
                story,
                node1,
                otherNodes: [node2Old, node3Old, node2New],
                transitions: [transOld1To2, transOld2To3, transNew1To2]);

            // Simulate session rewound back to Step 1 at Node 1 (with future histories at Step 2 and 3)
            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = node1.Id,
                CurrentStep = 1, // Rewound to step 1
                Status = StoryReadingStatus.InProgress
            };

            var historyStep1 = new StoryReadingHistory { ReadingSessionId = sessionId, StepNumber = 1, FromNodeId = node1.Id, TransitionId = transOld1To2.Id, ToNodeId = node1.Id };
            var historyStep2 = new StoryReadingHistory { ReadingSessionId = sessionId, StepNumber = 2, FromNodeId = node1.Id, TransitionId = transOld1To2.Id, ToNodeId = node2Old.Id };
            var historyStep3 = new StoryReadingHistory { ReadingSessionId = sessionId, StepNumber = 3, FromNodeId = node2Old.Id, TransitionId = transOld2To3.Id, ToNodeId = node3Old.Id };

            db.StoryReadingSessions.Add(session);
            db.StoryReadingHistories.AddRange(historyStep1, historyStep2, historyStep3);
            await db.SaveChangesAsync();
        }

        // Act: choose the new transition from Node 1
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/choices", token);
        request.Content = JsonContent.Create(new ChoiceRequest(node1.Id, transNew1To2.Id));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.StoryReadingSessions.FirstAsync(s => s.Id == sessionId);
            Assert.Equal(node2New.Id, session.CurrentNodeId);
            Assert.Equal(2, session.CurrentStep);

            var histories = await db.StoryReadingHistories
                .Where(h => h.ReadingSessionId == sessionId)
                .OrderBy(h => h.StepNumber)
                .ToListAsync();

            // Step 1 preserved, old Step 2 & 3 deleted, new Step 2 inserted
            Assert.Equal(2, histories.Count);
            Assert.Equal(1, histories[0].StepNumber);

            Assert.Equal(2, histories[1].StepNumber);
            Assert.Equal(node1.Id, histories[1].FromNodeId);
            Assert.Equal(transNew1To2.Id, histories[1].TransitionId);
            Assert.Equal(node2New.Id, histories[1].ToNodeId);
        }
    }

    [Fact]
    public async Task Choice_ConcurrentDoubleSubmit_AllowsExactlyOneRequest()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Concurrency Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Concurrency Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Concurrency Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };
        var targetNode = new StoryNode { StoryId = story.Id, Title = "Target Node", PartNumber = 2 };

        var transition = new StoryTransition
        {
            FromNodeId = rootNode.Id,
            ToNodeId = targetNode.Id,
            ChoiceTitle = "Cross the bridge",
            ChoiceIntent = "Cross",
            SortOrder = 1
        };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [targetNode], transitions: [transition]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = rootNode.Id,
                CurrentStep = 0,
                Status = StoryReadingStatus.InProgress
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act: Send two requests concurrently
        var req1 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/choices", token);
        req1.Content = JsonContent.Create(new ChoiceRequest(rootNode.Id, transition.Id));

        var req2 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/choices", token);
        req2.Content = JsonContent.Create(new ChoiceRequest(rootNode.Id, transition.Id));

        var task1 = _client.SendAsync(req1);
        var task2 = _client.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);

        // Assert: Exactly one 200 OK and exactly one 409 Conflict
        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        // Database assertions: exactly 1 step increment and exactly 1 history record
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.StoryReadingSessions.FirstAsync(s => s.Id == sessionId);
            Assert.Equal(targetNode.Id, session.CurrentNodeId);
            Assert.Equal(1, session.CurrentStep);

            var histories = await db.StoryReadingHistories.Where(h => h.ReadingSessionId == sessionId).ToListAsync();
            Assert.Single(histories);
            Assert.Equal(1, histories[0].StepNumber);
        }
    }
}
