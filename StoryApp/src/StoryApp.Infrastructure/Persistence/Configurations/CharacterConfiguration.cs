using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> builder)
    {
        builder.ToTable("Characters");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.Personality)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Motivation)
            .HasMaxLength(500);

        builder.Property(x => x.SpeakingStyle)
            .HasMaxLength(500);

        builder.Property(x => x.VisualDescription)
            .HasMaxLength(1000);

        builder.Property(x => x.SpecialAbility)
            .HasMaxLength(500);

        builder.HasIndex(x => x.StoryUniverseId);
        builder.HasIndex(x => x.RegionId);

        builder.HasIndex(x => new { x.StoryUniverseId, x.Code })
            .IsUnique();

        builder.HasOne(x => x.StoryUniverse)
            .WithMany(x => x.Characters)
            .HasForeignKey(x => x.StoryUniverseId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Region)
            .WithMany(x => x.Characters)
            .HasForeignKey(x => x.RegionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
