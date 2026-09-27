using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class StoryObjectConfiguration : IEntityTypeConfiguration<StoryObject>
{
    public void Configure(EntityTypeBuilder<StoryObject> builder)
    {
        builder.ToTable("StoryObjects");

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

        builder.Property(x => x.VisualDescription)
            .HasMaxLength(1000);

        builder.Property(x => x.StoryFunction)
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.StoryUniverseId, x.Code })
            .IsUnique();

        builder.HasOne(x => x.StoryUniverse)
            .WithMany(x => x.Objects)
            .HasForeignKey(x => x.StoryUniverseId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
