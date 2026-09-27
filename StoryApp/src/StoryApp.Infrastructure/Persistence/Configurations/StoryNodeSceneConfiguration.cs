using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryApp.Domain.Entities;

namespace StoryApp.Infrastructure.Persistence.Configurations;

public class StoryNodeSceneConfiguration : IEntityTypeConfiguration<StoryNodeScene>
{
    public void Configure(EntityTypeBuilder<StoryNodeScene> builder)
    {
        builder.ToTable("StoryNodeScenes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Text)
            .IsRequired();

        builder.Property(x => x.ImageObjectKey)
            .HasMaxLength(500);

        builder.Property(x => x.SortOrder)
            .IsRequired();

        builder.HasIndex(x => new { x.StoryNodeId, x.SortOrder })
            .IsUnique();

        builder.HasOne(x => x.StoryNode)
            .WithMany(x => x.Scenes)
            .HasForeignKey(x => x.StoryNodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
