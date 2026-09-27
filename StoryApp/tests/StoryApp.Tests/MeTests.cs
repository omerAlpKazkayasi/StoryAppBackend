using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using StoryApp.Api.Authentication;
using StoryApp.Application.Auth;
using StoryApp.Domain.Enums;
using Xunit;

namespace StoryApp.Tests;

public class MeTests : IClassFixture<AuthTestFixture>
{
    private readonly AuthTestFixture _factory;
    private readonly HttpClient _client;

    public MeTests(AuthTestFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithInvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.jwt.token");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithGuestAccessToken_ReturnsCurrentUser()
    {
        // 1. Create Guest user
        var guestRes = await _client.PostAsync("/api/auth/guest", null);
        var auth = await guestRes.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);

        // 2. Call GET /api/me with valid Bearer token
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(me);

        // ID must match the authenticated guest user
        Assert.Equal(auth.User.Id, me.Id);
    }

    [Fact]
    public async Task Me_WithGuestAccessToken_ReturnsGuestAccountType()
    {
        // 1. Create Guest user
        var guestRes = await _client.PostAsync("/api/auth/guest", null);
        var auth = await guestRes.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);

        // 2. Call GET /api/me
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(me);
        Assert.Equal("Guest", me.AccountType);
    }

    [Fact]
    public async Task Me_DoesNotAcceptUserIdFromRequest_AlwaysUsesJwtIdentity()
    {
        // 1. Create Guest user
        var guestRes = await _client.PostAsync("/api/auth/guest", null);
        var auth = await guestRes.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);

        var spoofedUserId = Guid.NewGuid();

        // 2. Try to pass spoofed userId via query parameter
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/me?userId={spoofedUserId}&id={spoofedUserId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(me);

        // Endpoint MUST ignore query parameter and use authenticated token's userId
        Assert.Equal(auth.User.Id, me.Id);
        Assert.NotEqual(spoofedUserId, me.Id);
    }

    [Fact]
    public void CurrentUser_WhenNotAuthenticated_ReturnsDefaultValues()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        var currentUser = new CurrentUser(httpContextAccessor);

        Assert.False(currentUser.IsAuthenticated);
        Assert.Equal(Guid.Empty, currentUser.UserId);
        Assert.Equal(UserAccountType.Guest, currentUser.AccountType);
    }

    [Fact]
    public void CurrentUser_WhenAuthenticated_ParsesSubAndAccountTypeClaims()
    {
        var expectedUserId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("sub", expectedUserId.ToString()),
            new Claim("account_type", "Registered")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = context
        };

        var currentUser = new CurrentUser(httpContextAccessor);

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(expectedUserId, currentUser.UserId);
        Assert.Equal(UserAccountType.Registered, currentUser.AccountType);
    }

    [Fact]
    public void CurrentUser_WhenAuthenticated_ThrowsIfSubClaimMissing()
    {
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("account_type", "Guest")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = context
        };

        var currentUser = new CurrentUser(httpContextAccessor);

        Assert.True(currentUser.IsAuthenticated);
        Assert.Throws<InvalidOperationException>(() => _ = currentUser.UserId);
    }

    [Fact]
    public void CurrentUser_WhenAuthenticated_ThrowsIfAccountTypeClaimMissing()
    {
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = context
        };

        var currentUser = new CurrentUser(httpContextAccessor);

        Assert.True(currentUser.IsAuthenticated);
        Assert.Throws<InvalidOperationException>(() => _ = currentUser.AccountType);
    }
}
