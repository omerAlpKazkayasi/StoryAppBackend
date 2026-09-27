using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    public void Configure(EntityTypeBuilder<Region> builder)
    {
        builder.ToTable("Regions");

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

        builder.Property(x => x.Atmosphere)
            .HasMaxLength(1000);

        builder.Property(x => x.VisualStyle)
            .HasMaxLength(1000);

        builder.Property(x => x.NaturalElements)
            .HasMaxLength(1000);

        builder.HasIndex(x => x.StoryUniverseId);

        builder.HasIndex(x => new { x.StoryUniverseId, x.Code })
            .IsUnique();

        builder.HasOne(x => x.StoryUniverse)
            .WithMany(x => x.Regions)
            .HasForeignKey(x => x.StoryUniverseId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
