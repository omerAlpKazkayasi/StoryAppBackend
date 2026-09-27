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

public class PlayProfileFilterTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_PlayProfileTests_{Guid.NewGuid():N}";
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

public class PlayProfileFilterTests : IClassFixture<PlayProfileFilterTestFixture>
{
    private readonly PlayProfileFilterTestFixture _factory;
    private readonly HttpClient _client;

    public PlayProfileFilterTests(PlayProfileFilterTestFixture factory)
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

    private async Task SetUserProfileAsync(string token, int? childAge, string? language)
    {
        using var req = CreateAuthenticatedRequest(HttpMethod.Put, "/api/me/profile", token);
        req.Content = JsonContent.Create(new UpdateUserProfileRequest(childAge, language));
        var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }

    private static async Task<Story> CreatePlayableStoryAsync(
        AppDbContext db,
        StoryUniverse universe,
        string title,
        int childAge,
        string language,
        StoryStatus status = StoryStatus.Ready)
    {
        var story = new Story
        {
            UniverseId = universe.Id,
            Title = title,
            ChildAge = childAge,
            Language = language,
            Status = status
        };
        db.Stories.Add(story);
        await db.SaveChangesAsync();

        var rootNode = new StoryNode
        {
            StoryId = story.Id,
            Title = $"{title} Root",
            PartNumber = 1
        };
        db.StoryNodes.Add(rootNode);
        await db.SaveChangesAsync();

        story.RootNodeId = rootNode.Id;
        await db.SaveChangesAsync();

        return story;
    }

    [Fact]
    public async Task Play_WithoutProfile_PreservesExistingSelectionBehavior()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        var theme = new Theme { Name = "NoProfile Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "NoProfile Universe", IsActive = true, Description = "Desc", MinimumAge = 5, MaximumAge = 12 };

        Guid storyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.Add(theme);
            db.StoryUniverses.Add(universe);
            await db.SaveChangesAsync();

            var story = await CreatePlayableStoryAsync(db, universe, "Any Story", childAge: 9, language: "en");
            storyId = story.Id;
        }

        // Act: User without profile calls /api/play
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var playRes = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(playRes);
        Assert.Equal(storyId, playRes.StoryId);
    }

    [Fact]
    public async Task Play_WithChildAge_SelectsMatchingStory()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();
        await SetUserProfileAsync(token, childAge: 8, language: null);

        var theme = new Theme { Name = "Age Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Age Universe", IsActive = true, Description = "Desc", MinimumAge = 5, MaximumAge = 12 };

        Guid story8Id;
        Guid story10Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.Add(theme);
            db.StoryUniverses.Add(universe);
            await db.SaveChangesAsync();

            var s8 = await CreatePlayableStoryAsync(db, universe, "Age 8 Story", childAge: 8, language: "tr");
            var s10 = await CreatePlayableStoryAsync(db, universe, "Age 10 Story", childAge: 10, language: "tr");
            story8Id = s8.Id;
            story10Id = s10.Id;
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));
        var response = await _client.SendAsync(request);

        // Assert: Must select story with ChildAge = 8, not 10
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var playRes = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(playRes);
        Assert.Equal(story8Id, playRes.StoryId);
        Assert.NotEqual(story10Id, playRes.StoryId);
    }

    [Fact]
    public async Task Play_WithChildAge_RespectsUniverseAgeRange()
    {
        // Arrange: User ChildAge = 8.
        // Universe has MinimumAge = 9, MaximumAge = 12. Story has ChildAge = 8.
        var (token, _) = await CreateGuestUserAsync();
        await SetUserProfileAsync(token, childAge: 8, language: null);

        var theme = new Theme { Name = "Universe Age Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Older Universe", IsActive = true, Description = "Desc", MinimumAge = 9, MaximumAge = 12 };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.Add(theme);
            db.StoryUniverses.Add(universe);
            await db.SaveChangesAsync();

            await CreatePlayableStoryAsync(db, universe, "Mismatch Universe Story", childAge: 8, language: "tr");
        }

        // Act: Should return 409 Conflict because Universe MinimumAge (9) > ChildAge (8)
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Play_WithPreferredLanguage_SelectsMatchingLanguage()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();
        await SetUserProfileAsync(token, childAge: null, language: "tr");

        var theme = new Theme { Name = "Lang Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Lang Universe", IsActive = true, Description = "Desc", MinimumAge = 5, MaximumAge = 12 };

        Guid storyTrId;
        Guid storyEnId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.Add(theme);
            db.StoryUniverses.Add(universe);
            await db.SaveChangesAsync();

            var sTr = await CreatePlayableStoryAsync(db, universe, "Turkish Story", childAge: 8, language: "tr");
            var sEn = await CreatePlayableStoryAsync(db, universe, "English Story", childAge: 8, language: "en");
            storyTrId = sTr.Id;
            storyEnId = sEn.Id;
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));
        var response = await _client.SendAsync(request);

        // Assert: Must select Turkish story
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var playRes = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(playRes);
        Assert.Equal(storyTrId, playRes.StoryId);
        Assert.NotEqual(storyEnId, playRes.StoryId);
    }

    [Fact]
    public async Task Play_WithAgeAndLanguage_AppliesBothFilters()
    {
        // Arrange: User ChildAge = 8, PreferredLanguage = "tr"
        var (token, _) = await CreateGuestUserAsync();
        await SetUserProfileAsync(token, childAge: 8, language: "tr");

        var theme = new Theme { Name = "Combined Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Combined Universe", IsActive = true, Description = "Desc", MinimumAge = 5, MaximumAge = 12 };

        Guid targetStoryId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.Add(theme);
            db.StoryUniverses.Add(universe);
            await db.SaveChangesAsync();

            var sMatch = await CreatePlayableStoryAsync(db, universe, "Target: 8 TR", childAge: 8, language: "tr");
            await CreatePlayableStoryAsync(db, universe, "Diff Lang: 8 EN", childAge: 8, language: "en");
            await CreatePlayableStoryAsync(db, universe, "Diff Age: 10 TR", childAge: 10, language: "tr");
            targetStoryId = sMatch.Id;
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var playRes = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(playRes);
        Assert.Equal(targetStoryId, playRes.StoryId);
    }

    [Fact]
    public async Task Play_WhenNoProfileCompatibleStory_ReturnsConflict()
    {
        // Arrange: User ChildAge = 6, but only age 10 stories exist
        var (token, _) = await CreateGuestUserAsync();
        await SetUserProfileAsync(token, childAge: 6, language: "tr");

        var theme = new Theme { Name = "NoMatch Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "NoMatch Universe", IsActive = true, Description = "Desc", MinimumAge = 5, MaximumAge = 12 };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.Add(theme);
            db.StoryUniverses.Add(universe);
            await db.SaveChangesAsync();

            await CreatePlayableStoryAsync(db, universe, "Older Story", childAge: 10, language: "tr");
        }

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Play_CompletedFallback_StillRespectsProfileFilters()
    {
        // Arrange:
        // Profile: ChildAge = 8, Language = "tr"
        // Story A: ChildAge = 8, Language = "tr" -> ALREADY COMPLETED by this user
        // Story B: ChildAge = 10, Language = "tr" -> UNREAD, but incompatible age!
        // Story C: ChildAge = 8, Language = "en" -> UNREAD, but incompatible language!
        var (token, userId) = await CreateGuestUserAsync();
        await SetUserProfileAsync(token, childAge: 8, language: "tr");

        var theme = new Theme { Name = "Fallback Theme", IsActive = true };
        var universe = new StoryUniverse { ThemeId = theme.Id, Name = "Fallback Universe", IsActive = true, Description = "Desc", MinimumAge = 5, MaximumAge = 12 };

        Guid completedStoryAId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.Add(theme);
            db.StoryUniverses.Add(universe);
            await db.SaveChangesAsync();

            var storyA = await CreatePlayableStoryAsync(db, universe, "Story A (8 TR)", childAge: 8, language: "tr");
            await CreatePlayableStoryAsync(db, universe, "Story B (10 TR)", childAge: 10, language: "tr");
            await CreatePlayableStoryAsync(db, universe, "Story C (8 EN)", childAge: 8, language: "en");

            completedStoryAId = storyA.Id;

            // Mark Story A as completed by user
            var completedSession = new StoryReadingSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                StoryId = storyA.Id,
                CurrentNodeId = storyA.RootNodeId!.Value,
                Status = StoryReadingStatus.Completed,
                CompletedAt = DateTime.UtcNow
            };
            db.StoryReadingSessions.Add(completedSession);
            await db.SaveChangesAsync();
        }

        // Act: Fallback should replay Story A, NOT select Story B or Story C
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/play", token);
        request.Content = JsonContent.Create(new PlayRequest(theme.Id));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var playRes = await response.Content.ReadFromJsonAsync<ReadingSessionResponse>();
        Assert.NotNull(playRes);
        Assert.Equal(completedStoryAId, playRes.StoryId);
    }
}
