using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class StoryNodeMemoryConfiguration : IEntityTypeConfiguration<StoryNodeMemory>
{
    public void Configure(EntityTypeBuilder<StoryNodeMemory> builder)
    {
        builder.ToTable("StoryNodeMemories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Summary)
            .IsRequired();

        builder.Property(x => x.CurrentGoal)
            .HasMaxLength(500);

        builder.Property(x => x.CompanionCharacterIdsJson)
            .IsRequired();

        builder.Property(x => x.ActiveObjectIdsJson)
            .IsRequired();

        builder.Property(x => x.OpenThreadsJson)
            .IsRequired();

        builder.Property(x => x.ImportantEventsJson)
            .IsRequired();

        builder.Property(x => x.EstablishedFactsJson)
            .IsRequired();

        builder.Property(x => x.NarrativeAngleUsed)
            .HasMaxLength(250);

        builder.HasIndex(x => x.StoryNodeId)
            .IsUnique();

        builder.HasOne(x => x.StoryNode)
            .WithOne(x => x.Memory)
            .HasForeignKey<StoryNodeMemory>(x => x.StoryNodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
