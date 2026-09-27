using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryApp.Application.Auth;
using StoryApp.Application.Themes;
using StoryApp.Domain.Entities;
using StoryApp.Infrastructure.Persistence;
using Xunit;

namespace StoryApp.Tests;

public class ThemeTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"StoryAppDb_ThemeTests_{Guid.NewGuid():N}";
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

public class ThemeTests : IClassFixture<ThemeTestFixture>
{
    private readonly ThemeTestFixture _factory;
    private readonly HttpClient _client;

    public ThemeTests(ThemeTestFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> GetGuestAccessTokenAsync()
    {
        var guestRes = await _client.PostAsync("/api/auth/guest", null);
        guestRes.EnsureSuccessStatusCode();
        var auth = await guestRes.Content.ReadFromJsonAsync<AuthResponseDto>();
        return auth!.AccessToken;
    }

    [Fact]
    public async Task Themes_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/themes");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Themes_WithGuestToken_ReturnsOk()
    {
        // Arrange
        var token = await GetGuestAccessTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/themes");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var themes = await response.Content.ReadFromJsonAsync<List<ThemeResponse>>();
        Assert.NotNull(themes);
    }

    [Fact]
    public async Task SwaggerDocument_ContainsBearerSecurityRequirement()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"Bearer\"", json);
        Assert.DoesNotContain("\"security\": [\n    { }\n  ]", json);
    }

    [Fact]
    public async Task Themes_ReturnsOnlyActiveThemes_AndOrdersBySortOrderThenName()
    {
        // Arrange
        var adventureTheme = new Theme
        {
            Name = "Adventure",
            SortOrder = 2,
            IsActive = true,
            Description = "Exciting adventures",
            Icon = "icon-adventure",
            Color = "#E65100"
        };
        var animalsTheme = new Theme
        {
            Name = "Animals",
            SortOrder = 1,
            IsActive = true,
            Description = "Friendly animals",
            Icon = "icon-animals",
            Color = "#2E7D32"
        };
        var spaceTheme = new Theme
        {
            Name = "Space",
            SortOrder = 3,
            IsActive = false, // Inactive
            Description = "Distant galaxies",
            Icon = "icon-space",
            Color = "#1565C0"
        };
        var magicTheme = new Theme
        {
            Name = "Magic",
            SortOrder = 1,
            IsActive = true,
            Description = "Enchanted realms",
            Icon = "icon-magic",
            Color = "#6A1B9A"
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.AddRange(adventureTheme, animalsTheme, spaceTheme, magicTheme);
            await db.SaveChangesAsync();
        }

        var token = await GetGuestAccessTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/themes");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var themes = await response.Content.ReadFromJsonAsync<List<ThemeResponse>>();
        Assert.NotNull(themes);

        // Inactive theme "Space" must not be present
        Assert.DoesNotContain(themes, t => t.Name == "Space");

        // Active themes must be present
        var relevantThemes = themes
            .Where(t => t.Id == adventureTheme.Id || t.Id == animalsTheme.Id || t.Id == magicTheme.Id)
            .ToList();

        Assert.Equal(3, relevantThemes.Count);

        // Order: SortOrder 1 (Animals, Magic alphabetically), then SortOrder 2 (Adventure)
        Assert.Equal("Animals", relevantThemes[0].Name);
        Assert.Equal("Magic", relevantThemes[1].Name);
        Assert.Equal("Adventure", relevantThemes[2].Name);
    }

    [Fact]
    public async Task Themes_ReturnsExpectedContract()
    {
        // Arrange
        var testTheme = new Theme
        {
            Name = "Fantasy Contract Test",
            Description = "Description of contract",
            Icon = "icon-fantasy",
            Color = "#9C27B0",
            SortOrder = 10,
            IsActive = true
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Themes.Add(testTheme);
            await db.SaveChangesAsync();
        }

        var token = await GetGuestAccessTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/themes");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);
        var themes = await response.Content.ReadFromJsonAsync<List<ThemeResponse>>();

        // Assert
        Assert.NotNull(themes);
        var item = themes.FirstOrDefault(t => t.Id == testTheme.Id);
        Assert.NotNull(item);
        Assert.Equal(testTheme.Id, item.Id);
        Assert.Equal("Fantasy Contract Test", item.Name);
        Assert.Equal("Description of contract", item.Description);
        Assert.Equal("icon-fantasy", item.Icon);
        Assert.Equal("#9C27B0", item.Color);
        Assert.Equal(10, item.SortOrder);
    }
}
