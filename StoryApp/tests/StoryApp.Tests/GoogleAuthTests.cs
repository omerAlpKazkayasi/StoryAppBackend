using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryApp.Application.Auth;
using StoryApp.Domain.Entities;
using StoryApp.Domain.Enums;
using StoryApp.Infrastructure.Identity;
using StoryApp.Infrastructure.Persistence;
using Xunit;

namespace StoryApp.Tests;

public static class FakeGoogleTokenHelper
{
    public static string GetSubject(string idToken)
    {
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(idToken)));
        return $"sub-{tokenHash[..16]}";
    }

    public static string? GetEmail(string idToken)
    {
        if (idToken.Contains("no-email"))
        {
            return null;
        }

        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(idToken)));
        return $"user.{tokenHash[..10]}@example.com";
    }
}

public class FakeGoogleTokenValidator : IGoogleTokenValidator
{
    public Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken) || idToken == "invalid-google-token" || idToken == "completely-invalid-token")
        {
            return Task.FromResult<GoogleIdentity?>(null);
        }

        if (idToken.StartsWith("valid-token-"))
        {
            var isUnverified = idToken.Contains("unverified");
            var subject = FakeGoogleTokenHelper.GetSubject(idToken);
            var email = FakeGoogleTokenHelper.GetEmail(idToken);
            var verified = !isUnverified;

            return Task.FromResult<GoogleIdentity?>(new GoogleIdentity(subject, email, verified));
        }

        return Task.FromResult<GoogleIdentity?>(null);
    }
}

public class GoogleAuthTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_GoogleAuthTests_{Guid.NewGuid():N}";
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
        builder.UseSetting("Authentication:Google:ClientIds:0", "test-google-client-id");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IGoogleTokenValidator));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }
            services.AddSingleton<IGoogleTokenValidator, FakeGoogleTokenValidator>();
        });
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

public class GoogleAuthTests : IClassFixture<GoogleAuthTestFixture>
{
    private readonly GoogleAuthTestFixture _factory;
    private readonly HttpClient _client;

    public GoogleAuthTests(GoogleAuthTestFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<AuthResponseDto> CreateGuestUserAsync()
    {
        var response = await _client.PostAsync("/api/auth/guest", null);
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        return auth;
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    [Fact]
    public async Task GoogleUpgrade_WithoutStoryAppToken_ReturnsUnauthorized()
    {
        // Act
        var token = $"valid-token-unauth-{Guid.NewGuid():N}";
        var response = await _client.PostAsJsonAsync("/api/auth/google/upgrade", new GoogleAuthRequest(token));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GoogleUpgrade_WithEmptyToken_ReturnsBadRequest()
    {
        // Arrange
        var guest = await CreateGuestUserAsync();

        // Act
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        request.Content = JsonContent.Create(new GoogleAuthRequest(""));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GoogleUpgrade_WithInvalidGoogleToken_ReturnsUnauthorized()
    {
        // Arrange
        var guest = await CreateGuestUserAsync();

        // Act
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        request.Content = JsonContent.Create(new GoogleAuthRequest("invalid-google-token"));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GoogleUpgrade_GuestUser_PreservesSameUserId_AndChangesAccountTypeToRegistered()
    {
        // Arrange
        var guest = await CreateGuestUserAsync();
        var token = $"valid-token-sameuser-{Guid.NewGuid():N}";

        // Act
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        request.Content = JsonContent.Create(new GoogleAuthRequest(token));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(content);
        Assert.Equal(guest.User.Id, content.User.Id);
        Assert.Equal("Registered", content.User.AccountType);

        // Verify in DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync(guest.User.Id);
        Assert.NotNull(user);
        Assert.Equal(UserAccountType.Registered, user.AccountType);
        Assert.Equal(guest.User.Id, user.Id);
    }

    [Fact]
    public async Task GoogleUpgrade_AddsAspNetUserLogin()
    {
        // Arrange
        var guest = await CreateGuestUserAsync();
        var token = $"valid-token-login-{Guid.NewGuid():N}";

        // Act
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        request.Content = JsonContent.Create(new GoogleAuthRequest(token));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify in AspNetUserLogins
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var login = await db.UserLogins
            .FirstOrDefaultAsync(l => l.UserId == guest.User.Id && l.LoginProvider == "Google");

        Assert.NotNull(login);
        Assert.Equal(FakeGoogleTokenHelper.GetSubject(token), login.ProviderKey);
    }

    [Fact]
    public async Task GoogleUpgrade_WithVerifiedEmail_StoresEmailAsConfirmed()
    {
        // Arrange
        var guest = await CreateGuestUserAsync();
        var token = $"valid-token-verified-{Guid.NewGuid():N}";

        // Act
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        request.Content = JsonContent.Create(new GoogleAuthRequest(token));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync(guest.User.Id);
        Assert.NotNull(user);
        Assert.Equal(FakeGoogleTokenHelper.GetEmail(token), user.Email);
        Assert.True(user.EmailConfirmed);
    }

    [Fact]
    public async Task GoogleUpgrade_WithUnverifiedEmail_DoesNotMarkEmailConfirmed()
    {
        // Arrange
        var guest = await CreateGuestUserAsync();
        var token = $"valid-token-unverified-{Guid.NewGuid():N}";

        // Act
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        request.Content = JsonContent.Create(new GoogleAuthRequest(token));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync(guest.User.Id);
        Assert.NotNull(user);
        Assert.False(user.EmailConfirmed);
    }

    [Fact]
    public async Task GoogleUpgrade_PreservesExistingUserProfile()
    {
        // Arrange
        var guest = await CreateGuestUserAsync();
        var token = $"valid-token-profile-{Guid.NewGuid():N}";

        // Create profile for guest
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserProfiles.Add(new UserProfile
            {
                UserId = guest.User.Id,
                ChildAge = 8,
                PreferredLanguage = "tr",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Act - Upgrade to Google
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        request.Content = JsonContent.Create(new GoogleAuthRequest(token));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profile = await db.UserProfiles.FindAsync(guest.User.Id);
            Assert.NotNull(profile);
            Assert.Equal(8, profile.ChildAge);
            Assert.Equal("tr", profile.PreferredLanguage);
        }
    }

    [Fact]
    public async Task GoogleUpgrade_PreservesReadingSessionsAndHistory()
    {
        // Arrange
        var guest = await CreateGuestUserAsync();
        var token = $"valid-token-reading-{Guid.NewGuid():N}";

        var sessionId = Guid.NewGuid();
        var storyId = Guid.NewGuid();
        var rootNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var themeId = Guid.NewGuid();
        var universeId = Guid.NewGuid();
        var transitionId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var theme = new Theme { Id = themeId, Name = "Theme A", IsActive = true, CreatedAt = DateTime.UtcNow };
            var universe = new StoryUniverse { Id = universeId, ThemeId = themeId, Name = "Univ A", Description = "Desc", IsActive = true, MinimumAge = 3, MaximumAge = 12, CreatedAt = DateTime.UtcNow };
            var story = new Story { Id = storyId, UniverseId = universeId, Title = "Story A", Language = "en", Status = StoryStatus.Ready, CreatedAt = DateTime.UtcNow };
            var rootNode = new StoryNode { Id = rootNodeId, StoryId = storyId, Title = "Root", PartNumber = 1, CreatedAt = DateTime.UtcNow };
            var targetNode = new StoryNode { Id = targetNodeId, StoryId = storyId, Title = "Target", PartNumber = 2, CreatedAt = DateTime.UtcNow };
            var transition = new StoryTransition { Id = transitionId, FromNodeId = rootNodeId, ToNodeId = targetNodeId, ChoiceTitle = "Choice", ChoiceIntent = "Intent", SortOrder = 1 };

            db.Themes.Add(theme);
            db.StoryUniverses.Add(universe);
            db.Stories.Add(story);
            db.StoryNodes.Add(rootNode);
            db.StoryNodes.Add(targetNode);
            db.StoryTransitions.Add(transition);
            await db.SaveChangesAsync();

            story.RootNodeId = rootNodeId;

            var session = new StoryReadingSession
            {
                Id = sessionId,
                UserId = guest.User.Id,
                StoryId = storyId,
                CurrentNodeId = rootNodeId,
                CurrentStep = 0,
                Status = StoryReadingStatus.InProgress,
                StartedAt = DateTime.UtcNow,
                LastReadAt = DateTime.UtcNow
            };
            db.StoryReadingSessions.Add(session);

            var history = new StoryReadingHistory
            {
                Id = Guid.NewGuid(),
                ReadingSessionId = sessionId,
                StepNumber = 1,
                FromNodeId = rootNodeId,
                TransitionId = transitionId,
                ToNodeId = targetNodeId,
                SelectedAt = DateTime.UtcNow
            };
            db.StoryReadingHistories.Add(history);
            await db.SaveChangesAsync();
        }

        // Act - Upgrade to Google
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        request.Content = JsonContent.Create(new GoogleAuthRequest(token));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.StoryReadingSessions.FindAsync(sessionId);
            Assert.NotNull(session);
            Assert.Equal(guest.User.Id, session.UserId);

            var histories = await db.StoryReadingHistories.Where(h => h.ReadingSessionId == sessionId).ToListAsync();
            Assert.NotEmpty(histories);
        }
    }

    [Fact]
    public async Task GoogleUpgrade_ReturnsRegisteredJwtClaims()
    {
        // Arrange
        var guest = await CreateGuestUserAsync();
        var token = $"valid-token-jwt-{Guid.NewGuid():N}";

        // Act
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        request.Content = JsonContent.Create(new GoogleAuthRequest(token));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(content);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(content.AccessToken);

        var subClaim = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        var accountTypeClaim = jwt.Claims.FirstOrDefault(c => c.Type == "account_type")?.Value;

        Assert.Equal(guest.User.Id.ToString(), subClaim);
        Assert.Equal("Registered", accountTypeClaim);
    }

    [Fact]
    public async Task GoogleUpgrade_RevokesOldGuestRefreshTokens()
    {
        // Arrange
        var guest = await CreateGuestUserAsync();
        var token = $"valid-token-revokeguest-{Guid.NewGuid():N}";

        // Act
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        request.Content = JsonContent.Create(new GoogleAuthRequest(token));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == guest.User.Id)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

        Assert.True(tokens.Count >= 2);
        // The initial guest token is revoked
        Assert.NotNull(tokens[0].RevokedAt);
        // The newly issued registered token is active
        Assert.Null(tokens.Last().RevokedAt);
    }

    [Fact]
    public async Task GoogleUpgrade_DoesNotStoreGoogleIdToken()
    {
        // Arrange
        var guest = await CreateGuestUserAsync();
        var rawGoogleToken = $"valid-token-nostore-{Guid.NewGuid():N}";

        // Act
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        request.Content = JsonContent.Create(new GoogleAuthRequest(rawGoogleToken));
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Check RefreshTokens
        var refreshTokens = await db.RefreshTokens.Where(t => t.UserId == guest.User.Id).ToListAsync();
        foreach (var rt in refreshTokens)
        {
            Assert.DoesNotContain(rawGoogleToken, rt.TokenHash);
        }

        // Check Users
        var user = await db.Users.FindAsync(guest.User.Id);
        Assert.NotNull(user);
        Assert.DoesNotContain(rawGoogleToken, user.UserName ?? string.Empty);
        Assert.DoesNotContain(rawGoogleToken, user.Email ?? string.Empty);

        // Check Logins
        var logins = await db.UserLogins.Where(l => l.UserId == guest.User.Id).ToListAsync();
        foreach (var l in logins)
        {
            Assert.NotEqual(rawGoogleToken, l.ProviderKey);
        }
    }

    [Fact]
    public async Task GoogleUpgrade_Collision_ReturnsConflict()
    {
        // Arrange: User A is already linked to shared collision token
        var collisionToken = $"valid-token-collision-{Guid.NewGuid():N}";

        var guestA = await CreateGuestUserAsync();
        var reqA = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guestA.AccessToken);
        reqA.Content = JsonContent.Create(new GoogleAuthRequest(collisionToken));
        var resA = await _client.SendAsync(reqA);
        Assert.Equal(HttpStatusCode.OK, resA.StatusCode);

        // Guest B tries to upgrade with the same Google token
        var guestB = await CreateGuestUserAsync();

        // Act
        var reqB = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guestB.AccessToken);
        reqB.Content = JsonContent.Create(new GoogleAuthRequest(collisionToken));
        var resB = await _client.SendAsync(reqB);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, resB.StatusCode);

        // Verify User A is unchanged and User B remains Guest
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userA = await db.Users.FindAsync(guestA.User.Id);
        Assert.NotNull(userA);
        Assert.Equal(UserAccountType.Registered, userA.AccountType);

        var userB = await db.Users.FindAsync(guestB.User.Id);
        Assert.NotNull(userB);
        Assert.Equal(UserAccountType.Guest, userB.AccountType);

        // User B has no Google login row
        var userBLogins = await db.UserLogins.Where(l => l.UserId == guestB.User.Id).ToListAsync();
        Assert.Empty(userBLogins);
    }

    [Fact]
    public async Task GoogleUpgrade_IdempotentRetry_ReturnsOk()
    {
        // Arrange: Guest A upgrades successfully
        var retryToken = $"valid-token-retry-{Guid.NewGuid():N}";

        var guestA = await CreateGuestUserAsync();
        var req1 = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guestA.AccessToken);
        req1.Content = JsonContent.Create(new GoogleAuthRequest(retryToken));
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Act: Same old Guest JWT retries the exact same upgrade
        var req2 = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guestA.AccessToken);
        req2.Content = JsonContent.Create(new GoogleAuthRequest(retryToken));
        var res2 = await _client.SendAsync(req2);

        // Assert
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var content = await res2.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(content);
        Assert.Equal(guestA.User.Id, content.User.Id);
        Assert.Equal("Registered", content.User.AccountType);

        // Ensure no duplicate AspNetUserLogins
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logins = await db.UserLogins
            .Where(l => l.UserId == guestA.User.Id && l.LoginProvider == "Google")
            .ToListAsync();
        Assert.Single(logins);
    }

    [Fact]
    public async Task GoogleLogin_ReturningUser_ReturnsOk()
    {
        // Arrange: User A is upgraded and linked to Google
        var loginToken = $"valid-token-returning-{Guid.NewGuid():N}";

        var guestA = await CreateGuestUserAsync();
        var reqUpgrade = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guestA.AccessToken);
        reqUpgrade.Content = JsonContent.Create(new GoogleAuthRequest(loginToken));
        var resUpgrade = await _client.SendAsync(reqUpgrade);
        Assert.Equal(HttpStatusCode.OK, resUpgrade.StatusCode);

        // Act: Returning login without any StoryApp bearer token
        var resLogin = await _client.PostAsJsonAsync("/api/auth/google", new GoogleAuthRequest(loginToken));

        // Assert
        Assert.Equal(HttpStatusCode.OK, resLogin.StatusCode);

        var content = await resLogin.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(content);
        Assert.Equal(guestA.User.Id, content.User.Id);
        Assert.Equal("Registered", content.User.AccountType);
        Assert.NotEmpty(content.AccessToken);
        Assert.NotEmpty(content.RefreshToken);
    }

    [Fact]
    public async Task GoogleLogin_UnknownAccount_ReturnsUnauthorized_AndDoesNotCreateUser()
    {
        // Arrange
        int initialUserCount;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            initialUserCount = await db.Users.CountAsync();
        }

        // Act: Valid Google token but not linked to any StoryApp user
        var unknownToken = $"valid-token-unknown-{Guid.NewGuid():N}";
        var response = await _client.PostAsJsonAsync("/api/auth/google", new GoogleAuthRequest(unknownToken));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var postUserCount = await db.Users.CountAsync();
            Assert.Equal(initialUserCount, postUserCount);

            var expectedSub = FakeGoogleTokenHelper.GetSubject(unknownToken);
            var login = await db.UserLogins.FirstOrDefaultAsync(l => l.ProviderKey == expectedSub);
            Assert.Null(login);
        }
    }

    [Fact]
    public async Task GoogleLogin_InvalidToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/google", new GoogleAuthRequest("completely-invalid-token"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GoogleLogin_DoesNotRevokeOtherDeviceTokens()
    {
        // Arrange: User A upgraded to Google
        var deviceToken = $"valid-token-multidevice-{Guid.NewGuid():N}";

        var guestA = await CreateGuestUserAsync();
        var reqUpgrade = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guestA.AccessToken);
        reqUpgrade.Content = JsonContent.Create(new GoogleAuthRequest(deviceToken));
        var resUpgrade = await _client.SendAsync(reqUpgrade);
        Assert.Equal(HttpStatusCode.OK, resUpgrade.StatusCode);

        // Act: Returning login from another device
        var resLogin = await _client.PostAsJsonAsync("/api/auth/google", new GoogleAuthRequest(deviceToken));
        Assert.Equal(HttpStatusCode.OK, resLogin.StatusCode);

        // Assert: Both refresh tokens remain active (unrevoked)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var activeTokens = await db.RefreshTokens
            .Where(t => t.UserId == guestA.User.Id && t.RevokedAt == null)
            .ToListAsync();

        Assert.True(activeTokens.Count >= 2);
    }

    [Fact]
    public async Task RefreshAfterUpgrade_ReturnsRegisteredClaim()
    {
        // Arrange: Upgrade guest
        var refreshUpgradeToken = $"valid-token-refreshup-{Guid.NewGuid():N}";

        var guest = await CreateGuestUserAsync();
        var reqUpgrade = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        reqUpgrade.Content = JsonContent.Create(new GoogleAuthRequest(refreshUpgradeToken));
        var resUpgrade = await _client.SendAsync(reqUpgrade);
        var upgradeAuth = await resUpgrade.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(upgradeAuth);

        // Act: Refresh using the newly issued refresh token
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(upgradeAuth.RefreshToken));

        // Assert
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var refreshedAuth = await refreshResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(refreshedAuth);
        Assert.Equal(guest.User.Id, refreshedAuth.User.Id);
        Assert.Equal("Registered", refreshedAuth.User.AccountType);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(refreshedAuth.AccessToken);
        var accountTypeClaim = token.Claims.FirstOrDefault(c => c.Type == "account_type")?.Value;
        Assert.Equal("Registered", accountTypeClaim);
    }

    [Fact]
    public async Task GoogleLogin_InactiveUser_ReturnsUnauthorized()
    {
        // Arrange: User is linked to Google but deactivated
        var inactiveToken = $"valid-token-inact-{Guid.NewGuid():N}";

        var guest = await CreateGuestUserAsync();
        var reqUpgrade = CreateAuthenticatedRequest(HttpMethod.Post, "/api/auth/google/upgrade", guest.AccessToken);
        reqUpgrade.Content = JsonContent.Create(new GoogleAuthRequest(inactiveToken));
        var resUpgrade = await _client.SendAsync(reqUpgrade);
        Assert.Equal(HttpStatusCode.OK, resUpgrade.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FindAsync(guest.User.Id);
            Assert.NotNull(user);
            user.IsActive = false;
            await db.SaveChangesAsync();
        }

        // Act: Attempt Google login
        var response = await _client.PostAsJsonAsync("/api/auth/google", new GoogleAuthRequest(inactiveToken));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
