using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryApp.Application.Auth;
using StoryApp.Domain.Enums;
using StoryApp.Infrastructure.Persistence;
using Xunit;

namespace StoryApp.Tests;

public class HardeningSecurityTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_HardeningTests_{Guid.NewGuid():N}";
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
            // Ignore cleanup failure in test fixture
        }

        await base.DisposeAsync();
    }
}

public class RateLimitTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_RateLimitTests_{Guid.NewGuid():N}";
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
        builder.UseSetting("RateLimiting:Auth:GuestPermitLimit", "2");
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
            // Ignore cleanup failure in test fixture
        }

        await base.DisposeAsync();
    }
}

public class HardeningSecurityTests : IClassFixture<HardeningSecurityTestFixture>
{
    private readonly HardeningSecurityTestFixture _factory;
    private readonly HttpClient _client;

    public HardeningSecurityTests(HardeningSecurityTestFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GoogleUpgrade_ConcurrentDifferentSubjects_AllowsOnlyOneSuccess_AndRejectsOtherWithConflict()
    {
        // 1. Create a guest user
        var guestRes = await _client.PostAsync("/api/auth/guest", null);
        Assert.Equal(HttpStatusCode.Created, guestRes.StatusCode);
        var guestAuth = await guestRes.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(guestAuth);

        using var client1 = _factory.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guestAuth.AccessToken);

        using var client2 = _factory.CreateClient();
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guestAuth.AccessToken);

        var tokenG1 = "valid-token-race-subject-1";
        var tokenG2 = "valid-token-race-subject-2";

        var startSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            return await client1.PostAsJsonAsync("/api/auth/google/upgrade", new GoogleAuthRequest(tokenG1));
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            return await client2.PostAsJsonAsync("/api/auth/google/upgrade", new GoogleAuthRequest(tokenG2));
        });

        startSignal.SetResult(true);

        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        // Exactly one 200 OK and exactly one 409 Conflict
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);
        Assert.Equal(1, statusCodes.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(1, statusCodes.Count(s => s == HttpStatusCode.Conflict));

        // Verify DB state: user is Registered, exactly 1 Google login exists for this user
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FindAsync(guestAuth.User.Id);
        Assert.NotNull(user);
        Assert.Equal(UserAccountType.Registered, user.AccountType);

        var logins = await db.UserLogins.Where(l => l.UserId == guestAuth.User.Id).ToListAsync();
        Assert.Single(logins);
        Assert.Equal("Google", logins[0].LoginProvider);
    }

    [Fact]
    public async Task GoogleUpgrade_WhenUserAlreadyRegistered_WithDifferentGoogleSubject_ReturnsConflict()
    {
        // 1. Create a guest user and upgrade with G1
        var guestRes = await _client.PostAsync("/api/auth/guest", null);
        Assert.Equal(HttpStatusCode.Created, guestRes.StatusCode);
        var guestAuth = await guestRes.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(guestAuth);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guestAuth.AccessToken);

        var tokenG1 = "valid-token-registered-user-g1";
        var upgrade1Res = await client.PostAsJsonAsync("/api/auth/google/upgrade", new GoogleAuthRequest(tokenG1));
        Assert.Equal(HttpStatusCode.OK, upgrade1Res.StatusCode);

        var upgradedAuth = await upgrade1Res.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(upgradedAuth);

        // 2. Attempt to upgrade the same registered user with a DIFFERENT Google account (G2)
        var tokenG2 = "valid-token-registered-user-g2";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", upgradedAuth.AccessToken);
        var upgrade2Res = await client.PostAsJsonAsync("/api/auth/google/upgrade", new GoogleAuthRequest(tokenG2));

        // Must return 409 Conflict
        Assert.Equal(HttpStatusCode.Conflict, upgrade2Res.StatusCode);

        // Verify DB state: existing G1 link is preserved, G2 is NOT added
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var logins = await db.UserLogins.Where(l => l.UserId == guestAuth.User.Id).ToListAsync();
        Assert.Single(logins);
        var expectedG1Subject = FakeGoogleTokenHelper.GetSubject(tokenG1);
        Assert.Equal(expectedG1Subject, logins[0].ProviderKey);
    }

    [Fact]
    public async Task RateLimiting_WhenGuestAuthLimitExceeded_Returns429TooManyRequests()
    {
        using var rateLimitFactory = new RateLimitTestFixture();
        await rateLimitFactory.InitializeAsync();
        try
        {
            using var client = rateLimitFactory.CreateClient();

            // 1st request -> 201 Created
            var res1 = await client.PostAsync("/api/auth/guest", null);
            Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

            // 2nd request -> 201 Created
            var res2 = await client.PostAsync("/api/auth/guest", null);
            Assert.Equal(HttpStatusCode.Created, res2.StatusCode);

            // 3rd request -> 429 Too Many Requests
            var res3 = await client.PostAsync("/api/auth/guest", null);
            Assert.Equal(HttpStatusCode.TooManyRequests, res3.StatusCode);

            var problem = await res3.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(429, problem.Status);
            Assert.Equal("Too Many Requests", problem.Title);
            Assert.NotNull(problem.Detail);
            Assert.True(problem.Extensions.ContainsKey("traceId"));
        }
        finally
        {
            await rateLimitFactory.DisposeAsync();
        }
    }

    [Fact]
    public void Startup_WhenJwtSigningKeyTooShort_ThrowsInvalidOperationException()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=(localdb)\\MSSQLLocalDB;Database=dummy;Trusted_Connection=True;");
            builder.UseSetting("Jwt:Issuer", "StoryApp.Test");
            builder.UseSetting("Jwt:Audience", "StoryApp.TestMobile");
            builder.UseSetting("Jwt:SigningKey", "too_short_key");
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenDays", "30");
        });

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            _ = factory.Services;
        });

        Assert.Contains("JWT signing key must be configured and at least 256 bits", exception.ToString());
    }
}
