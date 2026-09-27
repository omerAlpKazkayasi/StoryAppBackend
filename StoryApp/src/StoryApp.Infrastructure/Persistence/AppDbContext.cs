using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StoryApp.Domain.Entities;
using StoryApp.Infrastructure.Identity;

namespace StoryApp.Infrastructure.Persistence;

public class AppDbContext : IdentityUserContext<AppUser, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<Theme> Themes => Set<Theme>();
    public DbSet<StoryUniverse> StoryUniverses => Set<StoryUniverse>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<StoryObject> StoryObjects => Set<StoryObject>();
    public DbSet<EducationFact> EducationFacts => Set<EducationFact>();

    public DbSet<Story> Stories => Set<Story>();
    public DbSet<StoryNode> StoryNodes => Set<StoryNode>();
    public DbSet<StoryNodeScene> StoryNodeScenes => Set<StoryNodeScene>();
    public DbSet<StoryNodeMemory> StoryNodeMemories => Set<StoryNodeMemory>();
    public DbSet<StoryTransition> StoryTransitions => Set<StoryTransition>();

    public DbSet<StoryReadingSession> StoryReadingSessions => Set<StoryReadingSession>();
    public DbSet<StoryReadingHistory> StoryReadingHistories => Set<StoryReadingHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
