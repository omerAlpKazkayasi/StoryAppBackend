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

public class PlayTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_PlayTests_{Guid.NewGuid():N}";
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

public class PlayTests : IClassFixture<PlayTestFixture>
{
    private readonly PlayTestFixture _factory;
    private readonly HttpClient _client;

    public PlayTests(PlayTestFixture factory)
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
    public async Task Play_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/play", new PlayRequest(Guid.NewGuid()));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Play_WithEmptyThemeId_ReturnsBadRequest()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(Guid.Empty));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Play_WithUnknownTheme_ReturnsNotFound()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(Guid.NewGuid()));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Play_WithInactiveTheme_ReturnsNotFound()
    {
        // Arrange
        var theme = new Theme
        {
            Name = "Inactive Theme",
            IsActive = false
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.Add(theme);
            await db.SaveChangesAsync();
        }

        var (token, _) = await CreateGuestUserAsync();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Play_WithNoPlayableStory_ReturnsConflict()
    {
        // Arrange
        var theme = new Theme { Name = "Empty Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Empty Universe", Description = "Empty Universe Desc", IsActive = true };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.Add(theme);
            db.StoryUniverses.Add(universe);
            await db.SaveChangesAsync();
        }

        var (token, _) = await CreateGuestUserAsync();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Play_DoesNotSelectStoryFromInactiveUniverse()
    {
        // Arrange
        var theme = new Theme { Name = "Theme Inactive Universe", IsActive = true };
        var inactiveUniverse = new StoryUniverse { ThemeId = theme.Id, Name = "Inactive Universe", IsActive = false };
        var story = new Story
        {
            UniverseId = inactiveUniverse.Id,
            Title = "Story in Inactive Universe",
            Status = StoryStatus.Ready,
            Language = "tr",
            ChildAge = 7
        };
        var node = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, inactiveUniverse, story, node);
        }

        var (token, _) = await CreateGuestUserAsync();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Play_DoesNotSelectGeneratingStory()
    {
        // Arrange
        var theme = new Theme { Name = "Generating Story Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Gen Universe", IsActive = true };
        var story = new Story
        {
            UniverseId = universe.Id,
            Title = "Still Generating Story",
            Status = StoryStatus.Generating,
            Language = "tr",
            ChildAge = 7
        };
        var node = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, node);
        }

        var (token, _) = await CreateGuestUserAsync();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Play_DoesNotSelectFailedStoryEvenWhenRootNodeExists()
    {
        // Arrange
        var theme = new Theme { Name = "Failed Story Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Failed Universe", IsActive = true };
        var story = new Story
        {
            UniverseId = universe.Id,
            Title = "Failed Story With RootNode",
            Status = StoryStatus.Failed,
            FailureReason = "Generation failed mid-way",
            Language = "tr",
            ChildAge = 7
        };
        var node = new StoryNode { StoryId = story.Id, Title = "Root Node", PartNumber = 1 };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, story, node);
        }

        var (token, _) = await CreateGuestUserAsync();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Play_CreatesReadingSessionForAuthenticatedUser_AndReturnsExpectedContract()
    {
        // Arrange
        var theme = new Theme { Name = "Playable Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Playable Universe", IsActive = true };
        var story = new Story
        {
            UniverseId = universe.Id,
            Title = "The Grand Journey",
            Status = StoryStatus.Ready,
            Language = "tr",
            ChildAge = 8
        };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Chapter 1: The Beginning", PartNumber = 1, IsEnding = false };

        var scene1 = new StoryNodeScene { StoryNodeId = rootNode.Id, SortOrder = 2, Text = "Second scene text", ImageObjectKey = "img2.webp" };
        var scene2 = new StoryNodeScene { StoryNodeId = rootNode.Id, SortOrder = 1, Text = "First scene text", ImageObjectKey = "img1.webp" };

        var targetNode = new StoryNode { StoryId = story.Id, Title = "Chapter 2", PartNumber = 2 };

        var transitionValid = new StoryTransition
        {
            FromNodeId = rootNode.Id,
            ToNodeId = targetNode.Id,
            ChoiceTitle = "Enter the cave",
            ChoiceIntent = "Explore",
            SortOrder = 1
        };
        var transitionPending = new StoryTransition
        {
            FromNodeId = rootNode.Id,
            ToNodeId = null,
            ChoiceTitle = "Pending choice",
            ChoiceIntent = "Pending",
            SortOrder = 2
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(
                db,
                theme,
                universe,
                story,
                rootNode,
                otherNodes: [targetNode],
                scenes: [scene1, scene2],
                transitions: [transitionValid, transitionPending]);
        }

        var (token, userId) = await CreateGuestUserAsync();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var readingResponse = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(readingResponse);

        // Contract assertions
        Assert.Equal(story.Id, readingResponse.StoryId);
        Assert.Equal("The Grand Journey", readingResponse.StoryTitle);
        Assert.Equal("InProgress", readingResponse.Status);
        Assert.False(readingResponse.CanGoBack);

        // CurrentNode assertions
        var nodeRes = readingResponse.CurrentNode;
        Assert.NotNull(nodeRes);
        Assert.Equal(rootNode.Id, nodeRes.Id);
        Assert.Equal("Chapter 1: The Beginning", nodeRes.Title);
        Assert.False(nodeRes.IsEnding);

        // Scenes ordered by SortOrder ASC
        Assert.Equal(2, nodeRes.Scenes.Count);
        Assert.Equal(1, nodeRes.Scenes[0].SortOrder);
        Assert.Equal("First scene text", nodeRes.Scenes[0].Text);
        Assert.Equal(2, nodeRes.Scenes[1].SortOrder);
        Assert.Equal("Second scene text", nodeRes.Scenes[1].Text);

        // Only resolved choice (ToNodeId != null), pending choice excluded
        Assert.Single(nodeRes.Choices);
        Assert.Equal(transitionValid.Id, nodeRes.Choices[0].TransitionId);
        Assert.Equal("Enter the cave", nodeRes.Choices[0].Title);
        Assert.Equal(1, nodeRes.Choices[0].SortOrder);

        // Database persistence assertions
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var savedSession = await db.StoryReadingSessions.FirstOrDefaultAsync(s => s.Id == readingResponse.SessionId);
            Assert.NotNull(savedSession);
            Assert.Equal(userId, savedSession.UserId);
            Assert.Equal(story.Id, savedSession.StoryId);
            Assert.Equal(rootNode.Id, savedSession.CurrentNodeId);
            Assert.Equal(0, savedSession.CurrentStep);
            Assert.Equal(StoryReadingStatus.InProgress, savedSession.Status);
            Assert.Null(savedSession.CompletedAt);
        }
    }

    [Fact]
    public async Task Play_WhenRootNodeIsEnding_ReturnsEmptyChoices()
    {
        // Arrange
        var theme = new Theme { Name = "Ending Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Ending Universe", IsActive = true };
        var story = new Story
        {
            UniverseId = universe.Id,
            Title = "Short Tale",
            Status = StoryStatus.Ready,
            Language = "tr",
            ChildAge = 6
        };
        var rootNode = new StoryNode { StoryId = story.Id, Title = "Final Scene", PartNumber = 1, IsEnding = true };

        var targetNode = new StoryNode { StoryId = story.Id, Title = "Unreachable", PartNumber = 2 };
        var accidentalTransition = new StoryTransition
        {
            FromNodeId = rootNode.Id,
            ToNodeId = targetNode.Id,
            ChoiceTitle = "Ghost Choice",
            ChoiceIntent = "Ghost",
            SortOrder = 1
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(
                db,
                theme,
                universe,
                story,
                rootNode,
                otherNodes: [targetNode],
                transitions: [accidentalTransition]);
        }

        var (token, _) = await CreateGuestUserAsync();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var readingResponse = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(readingResponse);
        Assert.True(readingResponse.CurrentNode.IsEnding);
        Assert.Empty(readingResponse.CurrentNode.Choices);
    }

    [Fact]
    public async Task Play_PrefersStoryUserHasNotCompleted()
    {
        // Arrange
        var theme = new Theme { Name = "Unread Preference Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Pref Universe", Description = "Pref Universe Desc", IsActive = true };

        var storyA = new Story { UniverseId = universe.Id, Title = "Story A (Completed)", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var nodeA = new StoryNode { StoryId = storyA.Id, Title = "Node A", PartNumber = 1 };

        var storyB = new Story { UniverseId = universe.Id, Title = "Story B (Unread)", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var nodeB = new StoryNode { StoryId = storyB.Id, Title = "Node B", PartNumber = 1 };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.Add(theme);
            db.StoryUniverses.Add(universe);

            storyA.RootNodeId = null;
            storyB.RootNodeId = null;
            db.Stories.AddRange(storyA, storyB);
            db.StoryNodes.AddRange(nodeA, nodeB);
            await db.SaveChangesAsync();

            storyA.RootNodeId = nodeA.Id;
            storyB.RootNodeId = nodeB.Id;
            await db.SaveChangesAsync();
        }

        var (token, userId) = await CreateGuestUserAsync();

        // Mark Story A as Completed by this user
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var completedSession = new StoryReadingSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                StoryId = storyA.Id,
                CurrentNodeId = nodeA.Id,
                CurrentStep = 5,
                Status = StoryReadingStatus.Completed,
                StartedAt = DateTime.UtcNow.AddHours(-1),
                LastReadAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
            db.StoryReadingSessions.Add(completedSession);
            await db.SaveChangesAsync();
        }

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var readingResponse = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(readingResponse);
        // Must select Story B because Story A was already completed
        Assert.Equal(storyB.Id, readingResponse.StoryId);
    }

    [Fact]
    public async Task Play_AllowsCompletedStoryWhenNoUnreadStoryExists()
    {
        // Arrange
        var theme = new Theme { Name = "Repeat Allowed Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Repeat Universe", IsActive = true };

        var storyOnly = new Story { UniverseId = universe.Id, Title = "Only Story", Status = StoryStatus.Ready, Language = "tr", ChildAge = 8 };
        var nodeOnly = new StoryNode { StoryId = storyOnly.Id, Title = "Node Only", PartNumber = 1 };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SaveStoryGraphAsync(db, theme, universe, storyOnly, nodeOnly);
        }

        var (token, userId) = await CreateGuestUserAsync();

        // Mark only story as completed
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var completedSession = new StoryReadingSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                StoryId = storyOnly.Id,
                CurrentNodeId = nodeOnly.Id,
                CurrentStep = 4,
                Status = StoryReadingStatus.Completed,
                StartedAt = DateTime.UtcNow.AddHours(-2),
                LastReadAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
            db.StoryReadingSessions.Add(completedSession);
            await db.SaveChangesAsync();
        }

        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var readingResponse = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(readingResponse);
        // StoryOnly should be selected again as repeat fallback
        Assert.Equal(storyOnly.Id, readingResponse.StoryId);
    }
}
