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

public class ReadingBackTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_BackTests_{Guid.NewGuid():N}";
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

public class ReadingBackTests : IClassFixture<ReadingBackTestFixture>
{
    private readonly ReadingBackTestFixture _factory;
    private readonly HttpClient _client;

    public ReadingBackTests(ReadingBackTestFixture factory)
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
    public async Task Back_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.PostAsync($"/api/reading-sessions/{Guid.NewGuid()}/back", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Back_WithEmptySessionId_ReturnsBadRequest()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{Guid.Empty}/back", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Back_ForAnotherUsersSession_ReturnsNotFound()
    {
        // Arrange
        var (tokenOwner, userOwnerId) = await CreateGuestUserAsync();
        var (tokenOther, _) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Back Owner Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Back Owner Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Owner Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };
        var targetNode = new StoryNode { StoryId = story.Id, Title = "Target Node", PartNumber = 2 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [targetNode]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userOwnerId,
                StoryId = story.Id,
                CurrentNodeId = targetNode.Id,
                CurrentStep = 1,
                Status = StoryReadingStatus.InProgress
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act: other user attempts back
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/back", tokenOther);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Back_OnAbandonedSession_ReturnsConflict()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Abandoned Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Abandoned Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Abandoned Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };
        var targetNode = new StoryNode { StoryId = story.Id, Title = "Target Node", PartNumber = 2 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [targetNode]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = targetNode.Id,
                CurrentStep = 1,
                Status = StoryReadingStatus.Abandoned
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/back", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Back_AtRoot_ReturnsConflict()
    {
        // Arrange: session at root node (CurrentStep = 0)
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Root Back Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Root Back Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Root Back Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };

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
                CurrentStep = 0, // at root
                Status = StoryReadingStatus.InProgress
            };
            db.StoryReadingSessions.Add(session);
            await db.SaveChangesAsync();
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/back", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Database should be unmodified
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.StoryReadingSessions.FirstAsync(x => x.Id == sessionId);
            Assert.Equal(0, s.CurrentStep);
            Assert.Equal(rootNode.Id, s.CurrentNodeId);
        }
    }

    [Fact]
    public async Task Back_WithMissingOrInconsistentHistory_ReturnsConflict()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Inconsistent History Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Inconsistent Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Inconsistent Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var otherStory = new Story { UniverseId = universe.Id, Title = "Other Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };
        var nodeB = new StoryNode { StoryId = story.Id, Title = "Node B", PartNumber = 2 };
        var externalNode = new StoryNode { StoryId = otherStory.Id, Title = "External Node", PartNumber = 1 };

        var validTrans = new StoryTransition { FromNodeId = rootNode.Id, ToNodeId = nodeB.Id, ChoiceTitle = "Go", ChoiceIntent = "Go", SortOrder = 1 };

        Guid sessionMissingHistoryId = Guid.NewGuid();
        Guid sessionInconsistentToNodeId = Guid.NewGuid();
        Guid sessionExternalFromNodeId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [nodeB], transitions: [validTrans]);

            otherStory.RootNodeId = null;
            db.Stories.Add(otherStory);
            db.StoryNodes.Add(externalNode);
            await db.SaveChangesAsync();
            otherStory.RootNodeId = externalNode.Id;

            // Session 1: CurrentStep = 1 but no history in DB
            var session1 = new StoryReadingSession
            {
                Id = sessionMissingHistoryId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = nodeB.Id,
                CurrentStep = 1,
                Status = StoryReadingStatus.InProgress
            };

            // Session 2: CurrentStep = 1, history.ToNodeId != session.CurrentNodeId
            var session2 = new StoryReadingSession
            {
                Id = sessionInconsistentToNodeId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = nodeB.Id,
                CurrentStep = 1,
                Status = StoryReadingStatus.InProgress
            };
            var history2 = new StoryReadingHistory
            {
                ReadingSessionId = sessionInconsistentToNodeId,
                StepNumber = 1,
                FromNodeId = rootNode.Id,
                TransitionId = validTrans.Id,
                ToNodeId = rootNode.Id // Inconsistent with session2.CurrentNodeId (nodeB)
            };

            // Session 3: history.FromNodeId is externalNode (belongs to otherStory)
            var session3 = new StoryReadingSession
            {
                Id = sessionExternalFromNodeId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = nodeB.Id,
                CurrentStep = 1,
                Status = StoryReadingStatus.InProgress
            };
            var history3 = new StoryReadingHistory
            {
                ReadingSessionId = sessionExternalFromNodeId,
                StepNumber = 1,
                FromNodeId = externalNode.Id, // Not in session3.StoryId
                TransitionId = validTrans.Id,
                ToNodeId = nodeB.Id
            };

            db.StoryReadingSessions.AddRange(session1, session2, session3);
            db.StoryReadingHistories.AddRange(history2, history3);
            await db.SaveChangesAsync();
        }

        // Test 1: Missing history
        using var req1 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionMissingHistoryId}/back", token);
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Conflict, res1.StatusCode);

        // Test 2: Inconsistent ToNodeId
        using var req2 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionInconsistentToNodeId}/back", token);
        var res2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);

        // Test 3: FromNodeId from another story
        using var req3 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionExternalFromNodeId}/back", token);
        var res3 = await _client.SendAsync(req3);
        Assert.Equal(HttpStatusCode.Conflict, res3.StatusCode);
    }

    [Fact]
    public async Task Back_MovesToPreviousNode_DecrementsCurrentStep_AndDoesNotDeleteHistory()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Valid Back Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Valid Back Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Epic Tale", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var node1 = new StoryNode { StoryId = story.Id, Title = "Chapter 1: The Gate", PartNumber = 1 };
        var node2 = new StoryNode { StoryId = story.Id, Title = "Chapter 2: The Hall", PartNumber = 2 };
        var node3 = new StoryNode { StoryId = story.Id, Title = "Chapter 3: The Vault", PartNumber = 3 };

        var scene1Node2 = new StoryNodeScene { StoryNodeId = node2.Id, SortOrder = 1, Text = "Hall Scene 1", ImageObjectKey = "hall1.webp" };
        var transNode1To2 = new StoryTransition { FromNodeId = node1.Id, ToNodeId = node2.Id, ChoiceTitle = "Go to Hall", ChoiceIntent = "Hall", SortOrder = 1 };
        var transNode2To3 = new StoryTransition { FromNodeId = node2.Id, ToNodeId = node3.Id, ChoiceTitle = "Enter Vault", ChoiceIntent = "Enter", SortOrder = 1 };

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
                otherNodes: [node2, node3],
                scenes: [scene1Node2],
                transitions: [transNode1To2, transNode2To3]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = node3.Id,
                CurrentStep = 2,
                Status = StoryReadingStatus.InProgress,
                StartedAt = DateTime.UtcNow.AddMinutes(-10),
                LastReadAt = DateTime.UtcNow.AddMinutes(-5)
            };

            var historyStep1 = new StoryReadingHistory
            {
                ReadingSessionId = sessionId,
                StepNumber = 1,
                FromNodeId = node1.Id,
                TransitionId = transNode1To2.Id,
                ToNodeId = node2.Id,
                SelectedAt = DateTime.UtcNow.AddMinutes(-8)
            };

            var historyStep2 = new StoryReadingHistory
            {
                ReadingSessionId = sessionId,
                StepNumber = 2,
                FromNodeId = node2.Id,
                TransitionId = transNode2To3.Id,
                ToNodeId = node3.Id,
                SelectedAt = DateTime.UtcNow.AddMinutes(-5)
            };

            db.StoryReadingSessions.Add(session);
            db.StoryReadingHistories.AddRange(historyStep1, historyStep2);
            await db.SaveChangesAsync();
        }

        // Act: Back from Step 2 (node3) to Step 1 (node2)
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/back", token);
        var response = await _client.SendAsync(request);

        // Assert HTTP response
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var readingResponse = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(readingResponse);

        Assert.Equal(sessionId, readingResponse.SessionId);
        Assert.Equal(story.Id, readingResponse.StoryId);
        Assert.Equal("Epic Tale", readingResponse.StoryTitle);
        Assert.Equal("InProgress", readingResponse.Status);
        Assert.True(readingResponse.CanGoBack); // CurrentStep = 1 > 0 -> true

        // CurrentNode should be node2
        Assert.Equal(node2.Id, readingResponse.CurrentNode.Id);
        Assert.Equal("Chapter 2: The Hall", readingResponse.CurrentNode.Title);
        Assert.False(readingResponse.CurrentNode.IsEnding);

        // Previous node scenes & choices returned
        Assert.Single(readingResponse.CurrentNode.Scenes);
        Assert.Equal("Hall Scene 1", readingResponse.CurrentNode.Scenes[0].Text);
        Assert.Single(readingResponse.CurrentNode.Choices);
        Assert.Equal("Enter Vault", readingResponse.CurrentNode.Choices[0].Title);

        // Database assertions
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.StoryReadingSessions.FirstAsync(s => s.Id == sessionId);
            Assert.Equal(node2.Id, session.CurrentNodeId);
            Assert.Equal(1, session.CurrentStep);
            Assert.Equal(StoryReadingStatus.InProgress, session.Status);
            Assert.Null(session.CompletedAt);

            // History MUST NOT BE DELETED during back
            var histories = await db.StoryReadingHistories
                .Where(h => h.ReadingSessionId == sessionId)
                .OrderBy(h => h.StepNumber)
                .ToListAsync();

            Assert.Equal(2, histories.Count);
            Assert.Equal(1, histories[0].StepNumber);
            Assert.Equal(2, histories[1].StepNumber);
        }
    }

    [Fact]
    public async Task Back_FromCompletedSession_ReopensSession_AndClearsCompletedAt()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Completed Back Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Completed Back Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Finished Journey", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var rootNode = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1, IsEnding = false };
        var endingNode = new StoryNode { StoryId = story.Id, Title = "The End", PartNumber = 2, IsEnding = true };

        var transition = new StoryTransition { FromNodeId = rootNode.Id, ToNodeId = endingNode.Id, ChoiceTitle = "Finish", ChoiceIntent = "End", SortOrder = 1 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, rootNode, [endingNode], transitions: [transition]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = endingNode.Id,
                CurrentStep = 1,
                Status = StoryReadingStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-20),
                LastReadAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow.AddMinutes(-5)
            };

            var history = new StoryReadingHistory
            {
                ReadingSessionId = sessionId,
                StepNumber = 1,
                FromNodeId = rootNode.Id,
                TransitionId = transition.Id,
                ToNodeId = endingNode.Id,
                SelectedAt = DateTime.UtcNow.AddMinutes(-5)
            };

            db.StoryReadingSessions.Add(session);
            db.StoryReadingHistories.Add(history);
            await db.SaveChangesAsync();
        }

        // Act: Back from Completed session
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/back", token);
        var response = await _client.SendAsync(request);

        // Assert HTTP response
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var readingResponse = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(readingResponse);

        Assert.Equal("InProgress", readingResponse.Status);
        Assert.False(readingResponse.CanGoBack); // CurrentStep = 0 -> CanGoBack = false
        Assert.Equal(rootNode.Id, readingResponse.CurrentNode.Id);

        // Database assertions
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.StoryReadingSessions.FirstAsync(s => s.Id == sessionId);
            Assert.Equal(rootNode.Id, session.CurrentNodeId);
            Assert.Equal(0, session.CurrentStep);
            Assert.Equal(StoryReadingStatus.InProgress, session.Status);
            Assert.Null(session.CompletedAt); // CompletedAt must be cleared to null
        }
    }

    [Fact]
    public async Task Back_Twice_MovesBackTwoStepsSequentially()
    {
        // Arrange: 3 nodes (Root -> Node B -> Node C)
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Sequential Back Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Sequential Back Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Sequential Back Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var nodeA = new StoryNode { StoryId = story.Id, Title = "Node A (Root)", PartNumber = 1 };
        var nodeB = new StoryNode { StoryId = story.Id, Title = "Node B", PartNumber = 2 };
        var nodeC = new StoryNode { StoryId = story.Id, Title = "Node C", PartNumber = 3 };

        var transAtoB = new StoryTransition { FromNodeId = nodeA.Id, ToNodeId = nodeB.Id, ChoiceTitle = "To B", ChoiceIntent = "B", SortOrder = 1 };
        var transBtoC = new StoryTransition { FromNodeId = nodeB.Id, ToNodeId = nodeC.Id, ChoiceTitle = "To C", ChoiceIntent = "C", SortOrder = 1 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, nodeA, [nodeB, nodeC], transitions: [transAtoB, transBtoC]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = nodeC.Id,
                CurrentStep = 2,
                Status = StoryReadingStatus.InProgress
            };

            var h1 = new StoryReadingHistory { ReadingSessionId = sessionId, StepNumber = 1, FromNodeId = nodeA.Id, TransitionId = transAtoB.Id, ToNodeId = nodeB.Id };
            var h2 = new StoryReadingHistory { ReadingSessionId = sessionId, StepNumber = 2, FromNodeId = nodeB.Id, TransitionId = transBtoC.Id, ToNodeId = nodeC.Id };

            db.StoryReadingSessions.Add(session);
            db.StoryReadingHistories.AddRange(h1, h2);
            await db.SaveChangesAsync();
        }

        // Act 1: First back -> from Node C to Node B (step 2 -> 1)
        using var req1 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/back", token);
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var payload1 = await res1.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(payload1);
        Assert.Equal(nodeB.Id, payload1.CurrentNode.Id);
        Assert.True(payload1.CanGoBack);

        // Act 2: Second back -> from Node B to Node A (step 1 -> 0)
        using var req2 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/back", token);
        var res2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var payload2 = await res2.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(payload2);
        Assert.Equal(nodeA.Id, payload2.CurrentNode.Id);
        Assert.False(payload2.CanGoBack); // step 0 -> cannot go back

        // Act 3: Third back -> should return 409 Conflict because already at root
        using var req3 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/back", token);
        var res3 = await _client.SendAsync(req3);
        Assert.Equal(HttpStatusCode.Conflict, res3.StatusCode);

        // Both history records must still exist in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var count = await db.StoryReadingHistories.CountAsync(h => h.ReadingSessionId == sessionId);
            Assert.Equal(2, count);
        }
    }

    [Fact]
    public async Task Back_ConcurrentDoubleSubmit_AllowsExactlyOneRequest()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "Concurrent Back Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Concurrent Back Universe", IsActive = true };
        var story = new Story { UniverseId = universe.Id, Title = "Concurrent Back Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };

        var node1 = new StoryNode { StoryId = story.Id, Title = "Node 1", PartNumber = 1 };
        var node2 = new StoryNode { StoryId = story.Id, Title = "Node 2", PartNumber = 2 };

        var trans1To2 = new StoryTransition { FromNodeId = node1.Id, ToNodeId = node2.Id, ChoiceTitle = "Advance", ChoiceIntent = "Advance", SortOrder = 1 };

        Guid sessionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, node1, [node2], transitions: [trans1To2]);

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = userId,
                StoryId = story.Id,
                CurrentNodeId = node2.Id,
                CurrentStep = 1,
                Status = StoryReadingStatus.InProgress
            };

            var history = new StoryReadingHistory
            {
                ReadingSessionId = sessionId,
                StepNumber = 1,
                FromNodeId = node1.Id,
                TransitionId = trans1To2.Id,
                ToNodeId = node2.Id
            };

            db.StoryReadingSessions.Add(session);
            db.StoryReadingHistories.Add(history);
            await db.SaveChangesAsync();
        }

        // Act: Send two back requests concurrently
        var req1 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/back", token);
        var req2 = CreateAuthenticatedRequest(HttpMethod.Post, $"/api/reading-sessions/{sessionId}/back", token);

        var task1 = _client.SendAsync(req1);
        var task2 = _client.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);

        // Assert: Exactly one 200 OK and exactly one 409 Conflict
        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        // Database assertions: CurrentStep decremented exactly by 1 (to 0)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.StoryReadingSessions.FirstAsync(s => s.Id == sessionId);
            Assert.Equal(node1.Id, session.CurrentNodeId);
            Assert.Equal(0, session.CurrentStep);
        }
    }
}
