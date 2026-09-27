using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryApp.Application.Auth;
using StoryApp.Application.Profile;
using StoryApp.Infrastructure.Persistence;
using Xunit;

namespace StoryApp.Tests;

public class ProfileTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_ProfileTests_{Guid.NewGuid():N}";
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

public class ProfileTests : IClassFixture<ProfileTestFixture>
{
    private readonly ProfileTestFixture _factory;
    private readonly HttpClient _client;

    public ProfileTests(ProfileTestFixture factory)
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

    [Fact]
    public async Task Profile_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var getRes = await _client.GetAsync("/api/me/profile");
        var putRes = await _client.PutAsJsonAsync("/api/me/profile", new UpdateUserProfileRequest(8, "tr"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, getRes.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, putRes.StatusCode);
    }

    [Fact]
    public async Task Profile_Get_WhenMissing_ReturnsNullValues()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/me/profile", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        Assert.NotNull(profile);
        Assert.Null(profile.ChildAge);
        Assert.Null(profile.PreferredLanguage);
    }

    [Fact]
    public async Task Profile_Get_DoesNotCreateDatabaseRow()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        // Act: GET profile for user without a profile row
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/me/profile", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert: Database must not contain a UserProfile row for this user
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exists = await db.UserProfiles.AnyAsync(p => p.UserId == userId);
            Assert.False(exists);
        }
    }

    [Fact]
    public async Task Profile_Update_CreatesProfile_WhenNoneExists()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        // Act: PUT new profile
        var updateRequest = new UpdateUserProfileRequest(ChildAge: 7, PreferredLanguage: "en");
        using var request = CreateAuthenticatedRequest(HttpMethod.Put, "/api/me/profile", token);
        request.Content = JsonContent.Create(updateRequest);

        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profileResponse = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        Assert.NotNull(profileResponse);
        Assert.Equal(7, profileResponse.ChildAge);
        Assert.Equal("en", profileResponse.PreferredLanguage);

        // Database assertions
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileInDb = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            Assert.NotNull(profileInDb);
            Assert.Equal(7, profileInDb.ChildAge);
            Assert.Equal("en", profileInDb.PreferredLanguage);
            Assert.NotEqual(default, profileInDb.CreatedAt);
            Assert.Null(profileInDb.UpdatedAt); // Initial create has null UpdatedAt
        }
    }

    [Fact]
    public async Task Profile_Update_UpdatesExistingProfile()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        // Initial create
        var initial = new UpdateUserProfileRequest(ChildAge: 6, PreferredLanguage: "tr");
        using (var req1 = CreateAuthenticatedRequest(HttpMethod.Put, "/api/me/profile", token))
        {
            req1.Content = JsonContent.Create(initial);
            var res1 = await _client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        }

        DateTime createdAt;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.UserProfiles.FirstAsync(x => x.UserId == userId);
            createdAt = p.CreatedAt;
        }

        // Act: Update to new values
        var updated = new UpdateUserProfileRequest(ChildAge: 8, PreferredLanguage: "de");
        using (var req2 = CreateAuthenticatedRequest(HttpMethod.Put, "/api/me/profile", token))
        {
            req2.Content = JsonContent.Create(updated);
            var res2 = await _client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
            var payload = await res2.Content.ReadFromJsonAsync<UserProfileResponse>();
            Assert.NotNull(payload);
            Assert.Equal(8, payload.ChildAge);
            Assert.Equal("de", payload.PreferredLanguage);
        }

        // Database assertions
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.UserProfiles.FirstAsync(x => x.UserId == userId);
            Assert.Equal(8, p.ChildAge);
            Assert.Equal("de", p.PreferredLanguage);
            Assert.Equal(createdAt, p.CreatedAt); // CreatedAt preserved
            Assert.NotNull(p.UpdatedAt); // UpdatedAt populated
        }
    }

    [Fact]
    public async Task Profile_Update_NormalizesLanguage_ToTrimmedLowercase()
    {
        // Arrange
        var (token, userId) = await CreateGuestUserAsync();

        // Act: Update with mixed-case and whitespace
        var reqObj = new UpdateUserProfileRequest(ChildAge: 9, PreferredLanguage: "  TR  ");
        using var request = CreateAuthenticatedRequest(HttpMethod.Put, "/api/me/profile", token);
        request.Content = JsonContent.Create(reqObj);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        Assert.NotNull(payload);
        Assert.Equal("tr", payload.PreferredLanguage);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.UserProfiles.FirstAsync(x => x.UserId == userId);
            Assert.Equal("tr", p.PreferredLanguage);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public async Task Profile_Update_WithZeroOrNegativeAge_ReturnsBadRequest(int invalidAge)
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        var reqObj = new UpdateUserProfileRequest(ChildAge: invalidAge, PreferredLanguage: "tr");
        using var request = CreateAuthenticatedRequest(HttpMethod.Put, "/api/me/profile", token);
        request.Content = JsonContent.Create(reqObj);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Profile_Update_WithWhitespaceLanguage_ReturnsBadRequest(string whitespaceLang)
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        var reqObj = new UpdateUserProfileRequest(ChildAge: 5, PreferredLanguage: whitespaceLang);
        using var request = CreateAuthenticatedRequest(HttpMethod.Put, "/api/me/profile", token);
        request.Content = JsonContent.Create(reqObj);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Profile_Update_WithLanguageLongerThan10_ReturnsBadRequest()
    {
        // Arrange
        var (token, _) = await CreateGuestUserAsync();

        // Act
        var reqObj = new UpdateUserProfileRequest(ChildAge: 5, PreferredLanguage: "12345678901"); // 11 chars
        using var request = CreateAuthenticatedRequest(HttpMethod.Put, "/api/me/profile", token);
        request.Content = JsonContent.Create(reqObj);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Profile_Update_DoesNotAllowChangingAnotherUser()
    {
        // Arrange: User A and User B
        var (tokenA, userA) = await CreateGuestUserAsync();
        var (tokenB, userB) = await CreateGuestUserAsync();

        // Set User B profile
        using (var reqB = CreateAuthenticatedRequest(HttpMethod.Put, "/api/me/profile", tokenB))
        {
            reqB.Content = JsonContent.Create(new UpdateUserProfileRequest(ChildAge: 10, PreferredLanguage: "en"));
            var resB = await _client.SendAsync(reqB);
            Assert.Equal(HttpStatusCode.OK, resB.StatusCode);
        }

        // Act: User A updates profile
        using (var reqA = CreateAuthenticatedRequest(HttpMethod.Put, "/api/me/profile", tokenA))
        {
            reqA.Content = JsonContent.Create(new UpdateUserProfileRequest(ChildAge: 6, PreferredLanguage: "tr"));
            var resA = await _client.SendAsync(reqA);
            Assert.Equal(HttpStatusCode.OK, resA.StatusCode);
        }

        // Assert: User B profile is untouched
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pB = await db.UserProfiles.FirstAsync(x => x.UserId == userB);
            Assert.Equal(10, pB.ChildAge);
            Assert.Equal("en", pB.PreferredLanguage);

            var pA = await db.UserProfiles.FirstAsync(x => x.UserId == userA);
            Assert.Equal(6, pA.ChildAge);
            Assert.Equal("tr", pA.PreferredLanguage);
        }
    }
}
