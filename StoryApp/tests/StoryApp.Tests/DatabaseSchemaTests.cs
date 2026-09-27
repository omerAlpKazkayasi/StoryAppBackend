using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryApp.Domain.Entities;
using StoryApp.Infrastructure;
using StoryApp.Infrastructure.Persistence;
using Xunit;

namespace StoryApp.Tests;

public class DatabaseSchemaTests
{
    private const string ConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=StoryAppDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;";

    [Fact]
    public void AppDbContext_CanBeResolvedFromDependencyInjection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString
            })
            .Build();

        services.AddInfrastructure(configuration);
        using var serviceProvider = services.BuildServiceProvider();

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetService<AppDbContext>();

        Assert.NotNull(dbContext);
    }

    [Fact]
    public void EfCoreModel_MatchesExpectedDomainAndRelationalRules()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        using var context = new AppDbContext(options);
        var model = context.Model;

        // Verify entity types present
        Assert.NotNull(model.FindEntityType(typeof(Theme)));
        Assert.NotNull(model.FindEntityType(typeof(StoryUniverse)));
        Assert.NotNull(model.FindEntityType(typeof(Region)));
        Assert.NotNull(model.FindEntityType(typeof(Character)));
        Assert.NotNull(model.FindEntityType(typeof(StoryObject)));
        Assert.NotNull(model.FindEntityType(typeof(EducationFact)));
        Assert.NotNull(model.FindEntityType(typeof(Story)));
        Assert.NotNull(model.FindEntityType(typeof(StoryNode)));
        Assert.NotNull(model.FindEntityType(typeof(StoryNodeScene)));
        Assert.NotNull(model.FindEntityType(typeof(StoryNodeMemory)));
        Assert.NotNull(model.FindEntityType(typeof(StoryTransition)));
        Assert.NotNull(model.FindEntityType(typeof(StoryReadingSession)));
        Assert.NotNull(model.FindEntityType(typeof(StoryReadingHistory)));
        Assert.NotNull(model.FindEntityType(typeof(UserProfile)));
        Assert.NotNull(model.FindEntityType(typeof(StoryApp.Infrastructure.Identity.AppUser)));
        Assert.NotNull(model.FindEntityType(typeof(StoryApp.Infrastructure.Identity.RefreshToken)));

        // UserProfile checks
        var userProfileType = model.FindEntityType(typeof(UserProfile))!;
        Assert.Equal("UserId", userProfileType.FindPrimaryKey()!.Properties.Single().Name);
        Assert.Equal(10, userProfileType.FindProperty("PreferredLanguage")!.GetMaxLength());

        // Verify forbidden entities do NOT exist
        Assert.Null(model.FindEntityType("StoryApp.Domain.Entities.User"));
        Assert.Null(model.FindEntityType("StoryApp.Domain.Entities.StoryTypePartPlan"));
        Assert.Null(model.FindEntityType("Microsoft.AspNetCore.Identity.IdentityRole<System.Guid>"));

        // RefreshToken checks
        var refreshTokenEntity = model.FindEntityType(typeof(StoryApp.Infrastructure.Identity.RefreshToken))!;
        Assert.NotNull(refreshTokenEntity.FindProperty("TokenHash"));
        Assert.Null(refreshTokenEntity.FindProperty("Token"));
        Assert.Contains(refreshTokenEntity.GetIndexes(), i => i.IsUnique && i.Properties.Any(p => p.Name == "TokenHash"));

        // StoryNode checks
        var storyNodeType = model.FindEntityType(typeof(StoryNode))!;
        Assert.Null(storyNodeType.FindProperty("Text"));
        Assert.Null(storyNodeType.FindProperty("Momentum"));
        Assert.Null(storyNodeType.FindProperty("StoryNodeType"));
        Assert.NotNull(storyNodeType.FindProperty("Title"));

        var storyNodeUniqueIndex = storyNodeType.GetIndexes()
            .FirstOrDefault(i => i.IsUnique &&
                                 i.Properties.Select(p => p.Name).SequenceEqual(new[] { "StoryId", "PartNumber" }));
        Assert.NotNull(storyNodeUniqueIndex);

        // StoryNodeScene checks
        var sceneType = model.FindEntityType(typeof(StoryNodeScene))!;
        Assert.NotNull(sceneType.FindProperty("Text"));
        var sceneUniqueIndex = sceneType.GetIndexes()
            .FirstOrDefault(i => i.IsUnique &&
                                 i.Properties.Select(p => p.Name).SequenceEqual(new[] { "StoryNodeId", "SortOrder" }));
        Assert.NotNull(sceneUniqueIndex);

        // StoryTransition checks
        var transitionType = model.FindEntityType(typeof(StoryTransition))!;
        var toNodeProp = transitionType.FindProperty("ToNodeId")!;
        Assert.True(toNodeProp.IsNullable);

        var transitionUniqueIndex = transitionType.GetIndexes()
            .FirstOrDefault(i => i.IsUnique &&
                                 i.Properties.Select(p => p.Name).SequenceEqual(new[] { "FromNodeId", "SortOrder" }));
        Assert.NotNull(transitionUniqueIndex);

        // StoryReadingHistory checks
        var historyType = model.FindEntityType(typeof(StoryReadingHistory))!;
        var historyUniqueIndex = historyType.GetIndexes()
            .FirstOrDefault(i => i.IsUnique &&
                                 i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ReadingSessionId", "StepNumber" }));
        Assert.NotNull(historyUniqueIndex);
    }

    [Fact]
    public void SqlServerDatabase_HasExpectedTablesAndColumns()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        using var context = new AppDbContext(options);
        var canConnect = context.Database.CanConnect();
        Assert.True(canConnect, "Cannot connect to SQL Server database StoryAppDb on (localdb)\\MSSQLLocalDB");

        using var connection = context.Database.GetDbConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();

        // 1. Check __EFMigrationsHistory
        cmd.CommandText = "SELECT MigrationId FROM [__EFMigrationsHistory]";
        var appliedMigrations = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                appliedMigrations.Add(reader.GetString(0));
            }
        }
        Assert.Contains(appliedMigrations, m => m.EndsWith("InitialCreate"));
        Assert.Contains(appliedMigrations, m => m.EndsWith("AddGuestAuthentication"));
        Assert.Contains(appliedMigrations, m => m.EndsWith("AddUserProfile"));

        // 2. Check table list
        cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
        var tables = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.DoesNotContain("Users", tables);
        Assert.DoesNotContain("StoryTypePartPlans", tables);
        Assert.DoesNotContain("AspNetRoles", tables);
        Assert.DoesNotContain("AspNetUserRoles", tables);
        Assert.DoesNotContain("AspNetRoleClaims", tables);

        var expectedTables = new[]
        {
            "Themes", "StoryUniverses", "Regions", "Characters", "StoryObjects", "EducationFacts",
            "Stories", "StoryNodes", "StoryNodeScenes", "StoryNodeMemories", "StoryTransitions",
            "StoryReadingSessions", "StoryReadingHistories",
            "AspNetUsers", "RefreshTokens", "UserProfiles"
        };
        foreach (var expected in expectedTables)
        {
            Assert.Contains(expected, tables);
        }

        // Check RefreshTokens columns
        cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'RefreshTokens'";
        var refreshColumns = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                refreshColumns.Add(reader.GetString(0));
            }
        }
        Assert.Contains("TokenHash", refreshColumns);
        Assert.DoesNotContain("Token", refreshColumns);

        // Check UserProfiles columns
        cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UserProfiles'";
        var profileColumns = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                profileColumns.Add(reader.GetString(0));
            }
        }
        Assert.Contains("UserId", profileColumns);
        Assert.Contains("ChildAge", profileColumns);
        Assert.Contains("PreferredLanguage", profileColumns);
        Assert.Contains("CreatedAt", profileColumns);
        Assert.Contains("UpdatedAt", profileColumns);

        // 3. Check StoryNodes columns
        cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StoryNodes'";
        var nodeColumns = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                nodeColumns.Add(reader.GetString(0));
            }
        }
        Assert.DoesNotContain("Text", nodeColumns);
        Assert.DoesNotContain("Momentum", nodeColumns);

        // 4. Check StoryNodeScenes columns
        cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StoryNodeScenes'";
        var sceneColumns = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                sceneColumns.Add(reader.GetString(0));
            }
        }
        Assert.Contains("Text", sceneColumns);

        // 5. Check StoryTransitions.ToNodeId is nullable
        cmd.CommandText = "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StoryTransitions' AND COLUMN_NAME = 'ToNodeId'";
        var isNullable = (string)cmd.ExecuteScalar()!;
        Assert.Equal("YES", isNullable);
    }
}
