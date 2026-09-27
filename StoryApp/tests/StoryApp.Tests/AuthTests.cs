using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryApp.Application.Auth;
using StoryApp.Domain.Enums;
using StoryApp.Infrastructure.Identity;
using StoryApp.Infrastructure.Persistence;
using Xunit;

namespace StoryApp.Tests;

public class AuthTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_AuthTests_{Guid.NewGuid():N}";
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
            // Ignore cleanup failure in test dispose
        }

        await base.DisposeAsync();
    }
}

public class AuthTests : IClassFixture<AuthTestFixture>
{
    private readonly AuthTestFixture _factory;
    private readonly HttpClient _client;

    public AuthTests(AuthTestFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GuestAuth_CreatesGuestUser_AndReturnsAccessAndRefreshTokens()
    {
        // Act
        var response = await _client.PostAsync("/api/auth/guest", null);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(content);
        Assert.NotEmpty(content.AccessToken);
        Assert.NotEmpty(content.RefreshToken);
        Assert.Equal("Guest", content.User.AccountType);
        Assert.NotEqual(Guid.Empty, content.User.Id);

        // Verify in DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync(content.User.Id);
        Assert.NotNull(user);
        Assert.Equal(UserAccountType.Guest, user.AccountType);
        Assert.True(user.IsActive);
        Assert.StartsWith("guest_", user.UserName!);
    }

    [Fact]
    public async Task GuestAuth_StoresOnlyRefreshTokenHash_AndDoesNotStoreRawRefreshToken()
    {
        // Act
        var response = await _client.PostAsync("/api/auth/guest", null);
        var content = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(content);

        // Verify in DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokenRow = await db.RefreshTokens.FirstOrDefaultAsync(t => t.UserId == content.User.Id);

        Assert.NotNull(tokenRow);
        // Raw token must NEVER be stored in DB
        Assert.NotEqual(content.RefreshToken, tokenRow.TokenHash);
        // TokenHash must be 64-char hex string
        Assert.Equal(64, tokenRow.TokenHash.Length);

        // Verify that hashing raw token produces the exact TokenHash in DB
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content.RefreshToken)));
        Assert.Equal(expectedHash, tokenRow.TokenHash);
    }

    [Fact]
    public async Task Refresh_ReturnsNewAccessAndRefreshTokens_RevokesOldToken_AndPreventsReuse()
    {
        // 1. Create Guest
        var guestRes = await _client.PostAsync("/api/auth/guest", null);
        var auth1 = await guestRes.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth1);

        // 2. Refresh token
        var refreshReq = new RefreshTokenRequest(auth1.RefreshToken);
        var refreshRes = await _client.PostAsJsonAsync("/api/auth/refresh", refreshReq);

        Assert.Equal(HttpStatusCode.OK, refreshRes.StatusCode);
        var auth2 = await refreshRes.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth2);

        // New refresh token must differ from old one
        Assert.NotEqual(auth1.RefreshToken, auth2.RefreshToken);
        Assert.NotEqual(auth1.AccessToken, auth2.AccessToken);
        Assert.Equal(auth1.User.Id, auth2.User.Id);

        // 3. Verify DB state: old token revoked and linked, new token active
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var oldHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(auth1.RefreshToken)));
            var newHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(auth2.RefreshToken)));

            var oldToken = await db.RefreshTokens.FirstAsync(t => t.TokenHash == oldHash);
            var newToken = await db.RefreshTokens.FirstAsync(t => t.TokenHash == newHash);

            Assert.NotNull(oldToken.RevokedAt);
            Assert.Equal(newToken.Id, oldToken.ReplacedByTokenId);
            Assert.Null(newToken.RevokedAt);
        }

        // 4. Reusing old token MUST return 401 Unauthorized
        var reuseRes = await _client.PostAsJsonAsync("/api/auth/refresh", refreshReq);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseRes.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_ReturnsUnauthorized()
    {
        var randomToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        var refreshReq = new RefreshTokenRequest(randomToken);

        var response = await _client.PostAsJsonAsync("/api/auth/refresh", refreshReq);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_ReturnsUnauthorized()
    {
        // Create user and expired token directly in DB
        Guid userId = Guid.NewGuid();
        string rawToken = "expired_test_refresh_token_1234567890";
        string tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new AppUser
            {
                Id = userId,
                UserName = $"guest_{userId:N}",
                AccountType = UserAccountType.Guest,
                IsActive = true
            };
            db.Users.Add(user);

            var expiredToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = tokenHash,
                CreatedAt = DateTime.UtcNow.AddDays(-31),
                ExpiresAt = DateTime.UtcNow.AddDays(-1), // expired yesterday
                RevokedAt = null
            };
            db.RefreshTokens.Add(expiredToken);
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(rawToken));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken_AndIsIdempotent()
    {
        // 1. Create Guest
        var guestRes = await _client.PostAsync("/api/auth/guest", null);
        var auth = await guestRes.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);

        var logoutReq = new LogoutRequest(auth.RefreshToken);

        // 2. First logout -> 204 NoContent
        var logoutRes1 = await _client.PostAsJsonAsync("/api/auth/logout", logoutReq);
        Assert.Equal(HttpStatusCode.NoContent, logoutRes1.StatusCode);

        // Verify revoked in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(auth.RefreshToken)));
            var token = await db.RefreshTokens.FirstAsync(t => t.TokenHash == tokenHash);
            Assert.NotNull(token.RevokedAt);
        }

        // 3. Second logout with same token -> 204 NoContent (Idempotent)
        var logoutRes2 = await _client.PostAsJsonAsync("/api/auth/logout", logoutReq);
        Assert.Equal(HttpStatusCode.NoContent, logoutRes2.StatusCode);

        // 4. Logout with unknown token -> 204 NoContent (does not leak token existence)
        var logoutRes3 = await _client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest("nonexistent-token"));
        Assert.Equal(HttpStatusCode.NoContent, logoutRes3.StatusCode);
    }

    [Fact]
    public async Task JWT_ContainsExpectedSubjectAndAccountType_AndExpiration()
    {
        // Act
        var response = await _client.PostAsync("/api/auth/guest", null);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(auth.AccessToken);

        // Assert claims
        Assert.Equal(auth.User.Id.ToString(), jwt.Subject);
        Assert.Equal("Guest", jwt.Claims.First(c => c.Type == "account_type").Value);
        Assert.Equal("StoryApp.Test", jwt.Issuer);
        Assert.Contains("StoryApp.TestMobile", jwt.Audiences);

        // Assert expiration is approx 15 mins
        var diff = jwt.ValidTo - DateTime.UtcNow;
        Assert.True(diff.TotalMinutes is >= 14 and <= 16);
    }

    [Fact]
    public async Task Refresh_ConcurrentRequestsWithSameToken_OnlyOneSucceedsAndOtherReturns401()
    {
        // 1. Create Guest to obtain a valid refresh token
        var guestRes = await _client.PostAsync("/api/auth/guest", null);
        var auth = await guestRes.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);

        var oldRefreshToken = auth.RefreshToken;

        // 2. Prepare two concurrent HTTP clients and requests
        using var client1 = _factory.CreateClient();
        using var client2 = _factory.CreateClient();

        var startSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            return await client1.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(oldRefreshToken));
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            return await client2.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(oldRefreshToken));
        });

        // Release both requests at the exact same instant
        startSignal.SetResult(true);

        var responses = await Task.WhenAll(task1, task2);

        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        // Exactly one must be 200 OK and one must be 401 Unauthorized
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Unauthorized, statusCodes);
        Assert.Equal(1, statusCodes.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(1, statusCodes.Count(s => s == HttpStatusCode.Unauthorized));

        // 3. Verify Database state
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var oldTokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(oldRefreshToken)));
        var oldToken = await db.RefreshTokens.FirstAsync(t => t.TokenHash == oldTokenHash);

        // Old token must be revoked and linked to the replacement
        Assert.NotNull(oldToken.RevokedAt);
        Assert.NotNull(oldToken.ReplacedByTokenId);

        // Exactly one replacement token exists in the database
        var replacementTokens = await db.RefreshTokens
            .Where(t => t.Id == oldToken.ReplacedByTokenId)
            .ToListAsync();

        Assert.Single(replacementTokens);

        // Verify total tokens for this user is exactly 2 (the original guest token + exactly 1 replacement token)
        var allUserTokens = await db.RefreshTokens
            .Where(t => t.UserId == auth.User.Id)
            .ToListAsync();

        Assert.Equal(2, allUserTokens.Count);
    }
}
